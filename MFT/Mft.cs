using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MFT.Attributes;
using MFT.Other;
using Serilog;
using Serilog.Events;

namespace MFT;

public class Mft
{
    private readonly Dictionary<string, DirectoryNameMapValue> _directoryNameMap;
    private readonly Dictionary<string, HashSet<ParentMapEntry>> _parentDirectoryNameMap;

    //the directories (record keys) that have a directory in _parentDirectoryNameMap
    private readonly HashSet<string> _directoriesWithSubdirectories = new();

    private static readonly ParentMapEntry[] NoEntries = new ParentMapEntry[0];

    public Mft(Stream fileStream, bool recoverFromSlack)
    {
        FileRecords = new Dictionary<string, FileRecord>();
        FreeFileRecords = new Dictionary<string, FileRecord>();
        ExtensionFileRecords = new Dictionary<string, List<FileRecord>>();
        UnAssociatedExtensionFileRecords = new Dictionary<string, List<FileRecord>>();
        BadRecords = new List<FileRecord>();
        UninitializedRecords = new List<FileRecord>();

        FileSize = fileStream.Length;

        var headerBytes = new byte[4];

        if (fileStream.Length < 0x20 || ReadFully(fileStream, headerBytes, 4) != 4)
        {
            throw new Exception("Invalid header! Expected 'FILE' Signature.");
        }

        var sig = BitConverter.ToInt32(headerBytes, 0);
        if (sig != 0x454c4946) //Does not match FILE
        {
            throw new Exception("Invalid header! Expected 'FILE' Signature.");
        }

        var blockSizeBytes = new byte[4];


        fileStream.Seek(0x1c, SeekOrigin.Begin); //go where data is

        ReadFully(fileStream, blockSizeBytes, 4);
        fileStream.Seek(0, SeekOrigin.Begin); //reset to beginning

        var blockSize = BitConverter.ToInt32(blockSizeBytes, 0);

        //1024 normally, 4096 on 4Kn disks. A bad size of 0 used to loop forever
        if (blockSize < 0x100 || blockSize > 0x10000 || (blockSize & (blockSize - 1)) != 0)
        {
            throw new Exception($"Invalid FILE record size 0x{blockSize:X} in the first record (expected a power of two such as 0x400)");
        }

        var fileBytes = new byte[blockSize];

        long index = 0;

        while (fileStream.Position < fileStream.Length)
        {
            var read = ReadFully(fileStream, fileBytes, blockSize);

            if (read < blockSize)
            {
                //a partial last record: don't leave the previous record's bytes in the rest of the buffer
                Array.Clear(fileBytes, read, blockSize - read);
            }

            CurrentOffset = index;

            FileRecord f;
            try
            {
                f = new FileRecord(fileBytes, index,recoverFromSlack);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unable to process FILE record at offset 0x{Offset:X}: {Message}. Skipping...", index, e.Message);
                ParseErrors.Add($"FILE record at offset 0x{index:X} could not be processed: {e.Message}");
                index += blockSize;
                continue;
            }

            var key = f.GetKey();

            //the flags string is slow to build, so only when it will be written
            if (Log.IsEnabled(LogEventLevel.Verbose))
            {
                Log.Verbose("Offset: 0x{Offset:X} flags: {EntryFlags} key: {Key}", f.Offset,
                    f.EntryFlags.ToString().Replace(", ", "|"), key);
            }

            if (f.IsBad)
            {
                BadRecords.Add(f);
            }
            else if (f.IsUninitialized)
            {
                UninitializedRecords.Add(f);
            }
            else if (f.IsDeleted())
            {
                if (FreeFileRecords.ContainsKey(key))
                {
                    Log.Warning(
                        "At offset 0x{Offset:X}, a free FILE record with key '{Key}' already exists! You may want to review this manually. Skipping...",
                        f.Offset, key);
                }
                else
                {
                    FreeFileRecords.Add(key, f);
                }
            }
            else
            {
                if (FileRecords.ContainsKey(key))
                {
                    Log.Warning(
                        "At offset 0x{Offset:X}, a FILE record with key '{Key}' already exists! You may want to review this manually. Skipping...",
                        f.Offset, key);
                }
                else
                {
                    FileRecords.Add(key, f);
                }
            }

            if (f.IsUninitialized == false && f.IsBad == false && f.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                f.MftRecordToBaseRecord.MftSequenceNumber > 0)
            {
                //if the attribute list is NON-resident, have a fall back to get associated records
                var baseKey = f.MftRecordToBaseRecord.GetKey();

                if (ExtensionFileRecords.TryGetValue(baseKey, out var extensions) == false)
                {
                    extensions = new List<FileRecord>();
                    ExtensionFileRecords.Add(baseKey, extensions);
                }

                extensions.Add(f);
            }

            index += blockSize;
        }

        _directoryNameMap = new Dictionary<string, DirectoryNameMapValue>();
        _parentDirectoryNameMap = new Dictionary<string, HashSet<ParentMapEntry>>();

        CurrentOffset = index;

        ProcessExtensionBlocks();


        //  BuildDirectoryNameMap(FileRecords.Where(t => t.Value.IsDirectory()));
        // BuildDirectoryNameMap(FreeFileRecords.Where(t => t.Value.IsDirectory()));


        BuildMaps(FileRecords);
        BuildMaps(FreeFileRecords, false);
    }

    public long FileSize { get; }

    /// <summary>FILE records that could not be processed at all (problems inside a record are in its ParseErrors)</summary>
    public List<string> ParseErrors { get; } = new();

    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = stream.Read(buffer, total, count - total);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }


    public Dictionary<string, FileRecord> FileRecords { get; }
    private Dictionary<string, List<FileRecord>> ExtensionFileRecords { get; }
    private Dictionary<string, List<FileRecord>> UnAssociatedExtensionFileRecords { get; }
    public Dictionary<string, FileRecord> FreeFileRecords { get; }


    public List<FileRecord> BadRecords { get; }
    public List<FileRecord> UninitializedRecords { get; }

    /// <summary>
    ///     When the MFT is being processed, this is set to the offset where the FILE record being processed starts.
    ///     <remarks>Used to include the offset where errors happen in parsing for log messages</remarks>
    /// </summary>
    public static long CurrentOffset { get; private set; }

    private void BuildMaps(Dictionary<string, FileRecord> fileRecords, bool skipUnassociated = true)
    {
        foreach (var fileRecord in fileRecords)
        {
            if (fileRecord.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                fileRecord.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
                //will get this record via extensionRecord
            {
                continue;
            }

            if (fileRecord.Value.Attributes.Count == 0)
            {
                Log.Debug("Skipping file record at offset 0x{Offset:X} has no attributes", fileRecord.Value.Offset);
                continue;
            }

            var fileNameRecords = fileRecord.Value.Attributes.Where(t => t.AttributeType == AttributeType.FileName)
                .ToList();

            foreach (var fileNameRecord in fileNameRecords)
            {
                var fna = (FileName)fileNameRecord;
                if (fna.FileInfo.NameType == NameTypes.Dos)
                {
                    continue;
                }

                if (fileRecord.Value.IsDirectory())
                {
                    var keyDir = fileRecord.Value.GetKey();

                    if (_directoryNameMap.ContainsKey(keyDir) == false)
                    {
                        _directoryNameMap.Add(keyDir,
                            new DirectoryNameMapValue(fna.FileInfo.FileName, $"{fna.FileInfo.ParentMftRecord.GetKey()}",
                                fileRecord.Value.IsDeleted()));
                    }
                }

                AddToParentMap(fna, fileRecord.Value);
            }
        }

        if (skipUnassociated)
        {
            return;
        }

        foreach (var unAssociatedExtensionFileRecord in UnAssociatedExtensionFileRecords)
            // if (unAssociatedExtensionFileRecord.Value.Attributes.Count == 0)
            // {
            //     _logger.Debug($"Skipping file record with entry/seq #{unAssociatedExtensionFileRecord.Key} since it has no attributes");
            //     continue;
            // }


        foreach (var fileRecord in unAssociatedExtensionFileRecord.Value)
        {
            var fileNameRecords = fileRecord.Attributes.Where(t => t.AttributeType == AttributeType.FileName)
                .ToList();

            foreach (var fileNameRecord in fileNameRecords)
            {
                var fna = (FileName)fileNameRecord;
                if (fna.FileInfo.NameType == NameTypes.Dos)
                {
                    continue;
                }


                if (fileRecord.IsDirectory())
                {
                    //override this with the base record info
                    var keyDir =
                        $"{fileRecord.MftRecordToBaseRecord.MftEntryNumber:X8}-{fileRecord.MftRecordToBaseRecord.MftSequenceNumber:X8}"; // fileRecord.GetKey();

                    // if (fileRecord.IsDeleted())
                    // {
                    //     keyDir=    $"{fileRecord.EntryNumber:X8}-{fileRecord.SequenceNumber - 1:X8}";
                    // }


                    if (_directoryNameMap.ContainsKey(keyDir) == false)
                    {
                        _directoryNameMap.Add(keyDir,
                            new DirectoryNameMapValue(fna.FileInfo.FileName, $"{fna.FileInfo.ParentMftRecord.GetKey()}",
                                fileRecord.IsDeleted()));
                    }
                }

                AddToParentMap(fna, fileRecord);
            }
        }
    }

    /// <summary>
    ///     Adds a name of a record to its parent's entries (the parent gets a map entry even for ".", which is not added)
    /// </summary>
    private void AddToParentMap(FileName fna, FileRecord fileRecord)
    {
        var parentKey = fna.FileInfo.ParentMftRecord.GetKey();

        if (_parentDirectoryNameMap.TryGetValue(parentKey, out var entries) == false)
        {
            entries = new HashSet<ParentMapEntry>();
            _parentDirectoryNameMap.Add(parentKey, entries);
        }

        if (fna.FileInfo.FileName.Equals(".") == false)
        {
            var isDirectory = fileRecord.IsDirectory();

            entries.Add(new ParentMapEntry(fna.FileInfo.FileName, fileRecord.GetKey(true), isDirectory, fileRecord));

            if (isDirectory)
            {
                _directoriesWithSubdirectories.Add(parentKey);
            }
        }
    }

    /// <summary>
    ///     The names in a directory (key: <c>"{entry:X8}-{sequence:X8}"</c>, as in <see cref="FileRecords" />), directories
    ///     first, then by name. A new sorted list on every call: <see cref="GetDirectoryEntries" /> is the unsorted one
    /// </summary>
    public List<ParentMapEntry> GetDirectoryContents(string key)
    {
        if (_parentDirectoryNameMap.TryGetValue(key, out var entries))
        {
            return entries.OrderByDescending(t => t.IsDirectory).ThenBy(t => t.FileName).ToList();
        }

        return new List<ParentMapEntry>();
    }

    /// <summary>
    ///     The names in a directory (key: <c>"{entry:X8}-{sequence:X8}"</c>, as in <see cref="FileRecords" />), unsorted
    ///     (the same entries as <see cref="GetDirectoryContents" />, without building and sorting a list). Empty when the
    ///     directory has none
    /// </summary>
    public IReadOnlyCollection<ParentMapEntry> GetDirectoryEntries(string key)
    {
        if (key != null && _parentDirectoryNameMap.TryGetValue(key, out var entries))
        {
            return new ReadOnlyEntries(entries);
        }

        return NoEntries;
    }

    /// <summary>
    ///     Whether a directory (key: <c>"{entry:X8}-{sequence:X8}"</c>) has a directory among its entries
    ///     (<see cref="GetDirectoryEntries" />)
    /// </summary>
    public bool HasSubdirectories(string key)
    {
        return key != null && _directoriesWithSubdirectories.Contains(key);
    }

    //a directory's entries, without giving out the set itself
    private sealed class ReadOnlyEntries : IReadOnlyCollection<ParentMapEntry>
    {
        private readonly HashSet<ParentMapEntry> _entries;

        public ReadOnlyEntries(HashSet<ParentMapEntry> entries)
        {
            _entries = entries;
        }

        public int Count => _entries.Count;

        public IEnumerator<ParentMapEntry> GetEnumerator()
        {
            return _entries.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private void ProcessExtensionBlocks()
    {
        Log.Debug("Processing Extension FILE records");

        foreach (var fileRecord in ExtensionFileRecords)
        {
            FileRecord baseRecord = null;
            if (FileRecords.ContainsKey(fileRecord.Key))
            {
                baseRecord = FileRecords[fileRecord.Key];
            }
            else if (FreeFileRecords.ContainsKey(fileRecord.Key))
            {
                baseRecord = FreeFileRecords[fileRecord.Key];
            }

            if (baseRecord == null)
            {
                //we could not associate this record to its base record, so treat it as such
                //later we will check these when rebuilding parent paths.
                UnAssociatedExtensionFileRecords.Add(fileRecord.Key, new List<FileRecord>());

                UnAssociatedExtensionFileRecords[fileRecord.Key].AddRange(fileRecord.Value);

                continue;
            }

            Log.Debug("FILE record '{Key}', Extension records found: {Count:N0}", fileRecord.Key,
                fileRecord.Value.Count);

            //pull in all related attributes from this record for processing later
            foreach (var fileRecordAttribute in fileRecord.Value)
            {
                baseRecord.Attributes.AddRange(fileRecordAttribute.Attributes);
            }
        }
    }

    /// <summary>
    ///     Given an MFT entry # and seq #, return the full path
    /// </summary>
    /// <param name="recordKey"></param>
    /// <returns></returns>
    public string GetFullParentPath(string recordKey)
    {
        var stack = new Stack<string>();

        var tempKey = recordKey;

        //a damaged MFT can have directories that are each other's parent, which looped forever
        var seen = new HashSet<string>();

        while (_directoryNameMap.TryGetValue(tempKey, out var dir) && seen.Add(tempKey))
        {
            stack.Push(dir.Name);

            if (tempKey.Equals("00000005-00000005"))
                //all done since we are at root
            {
                break;
            }

            tempKey = dir.ParentRecordKey;
        }

        if (tempKey != "00000005-00000005")
            //we dropped out of our map too early, so adjust it
        {
            stack.Push($".\\PathUnknown\\Directory with ID 0x{tempKey}");
        }

        return string.Join("\\", stack);
    }

    /// <summary>
    ///     Creates a map for directories and their parent directories.
    /// </summary>
    /// <param name="fileRecords"></param>
    private void BuildDirectoryNameMap(IEnumerable<KeyValuePair<string, FileRecord>> fileRecords)
    {
        foreach (var fileRecord in fileRecords)
        {
            if (fileRecord.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                fileRecord.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
                //will get this record via extensionRecord
            {
                continue;
            }

            if (fileRecord.Value.Attributes.Count == 0)
            {
                Log.Debug("Skipping file record at offset 0x{Offset:X} has no attributes", fileRecord.Value.Offset);
                continue;
            }

            var fileNameRecords = fileRecord.Value.Attributes.Where(t => t.AttributeType == AttributeType.FileName)
                .ToList();

            foreach (var fileNameRecord in fileNameRecords)
            {
                var fna = (FileName)fileNameRecord;
                if (fna.FileInfo.NameType == NameTypes.Dos)
                {
                    continue;
                }

                var key = fileRecord.Value.GetKey();

                if (_directoryNameMap.ContainsKey(key) == false)
                {
                    _directoryNameMap.Add(key,
                        new DirectoryNameMapValue(fna.FileInfo.FileName, $"{fna.FileInfo.ParentMftRecord.GetKey()}",
                            fileRecord.Value.IsDeleted()));
                }
            }
        }

        foreach (var unAssociatedExtensionFileRecord in UnAssociatedExtensionFileRecords)
            // if (unAssociatedExtensionFileRecord.Value.Attributes.Count == 0)
            // {
            //     _logger.Debug($"Skipping file record with entry/seq #{unAssociatedExtensionFileRecord.Key} since it has no attributes");
            //     continue;
            // }


        foreach (var fileRecord in unAssociatedExtensionFileRecord.Value)
        {
            var fileNameRecords = fileRecord.Attributes.Where(t => t.AttributeType == AttributeType.FileName)
                .ToList();

            foreach (var fileNameRecord in fileNameRecords)
            {
                var fna = (FileName)fileNameRecord;
                if (fna.FileInfo.NameType == NameTypes.Dos)
                {
                    continue;
                }

                var key = fileRecord.GetKey();

                if (_directoryNameMap.ContainsKey(key) == false)
                {
                    _directoryNameMap.Add(key,
                        new DirectoryNameMapValue(fna.FileInfo.FileName, $"{fna.FileInfo.ParentMftRecord.GetKey()}",
                            fileRecord.IsDeleted()));
                }
            }
        }
    }
}