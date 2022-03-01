using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MFT.Attributes;
using MFT.Other;
using Serilog;

namespace MFT;

public class Mft
{
    private readonly Dictionary<string, DirectoryNameMapValue> _directoryNameMap;
    private readonly Dictionary<string, HashSet<ParentMapEntry>> _parentDirectoryNameMap;

    public Mft(Stream fileStream)
    {
        FileRecords = new Dictionary<string, FileRecord>();
        FreeFileRecords = new Dictionary<string, FileRecord>();
        ExtensionFileRecords = new Dictionary<string, List<FileRecord>>();
        UnAssociatedExtensionFileRecords = new Dictionary<string, List<FileRecord>>();
        BadRecords = new List<FileRecord>();
        UninitializedRecords = new List<FileRecord>();

        FileSize = fileStream.Length;

        var headerBytes = new byte[4];

        fileStream.Read(headerBytes, 0, 4);

        var sig = BitConverter.ToInt32(headerBytes, 0);
        if (sig != 0x454c4946) //Does not match FILE
        {
            throw new Exception("Invalid header! Expected 'FILE' Signature.");
        }

        var blockSizeBytes = new byte[4];


        fileStream.Seek(0x1c, SeekOrigin.Begin); //go where data is

        fileStream.Read(blockSizeBytes, 0, 4);
        fileStream.Seek(0, SeekOrigin.Begin); //reset to beginning

        var blockSize = BitConverter.ToInt32(blockSizeBytes, 0);

        var fileBytes = new byte[blockSize];

        var index = 0;

        while (fileStream.Position < fileStream.Length)
        {
            fileStream.Read(fileBytes, 0, blockSize);

            //  Buffer.BlockCopy(rawBytes, index, fileBytes, 0, blockSize);

            CurrentOffset = index;

            var f = new FileRecord(fileBytes, index);

            var key = f.GetKey();

            Log.Verbose("Offset: 0x{Offset:X} flags: {EntryFlags} key: {Key}", f.Offset,
                f.EntryFlags.ToString().Replace(", ", "|"), key);

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
                if (ExtensionFileRecords.ContainsKey(f.MftRecordToBaseRecord.GetKey()) == false)
                {
                    ExtensionFileRecords.Add(f.MftRecordToBaseRecord.GetKey(), new List<FileRecord>());
                }

                ExtensionFileRecords[f.MftRecordToBaseRecord.GetKey()].Add(f);
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
    public static int CurrentOffset { get; private set; }

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

                var key = fileRecord.Value.GetKey();
                var parentKey = fna.FileInfo.ParentMftRecord.GetKey();

                if (_parentDirectoryNameMap.ContainsKey(parentKey) == false)
                {
                    _parentDirectoryNameMap.Add(parentKey, new HashSet<ParentMapEntry>());
                }

                if (fna.FileInfo.FileName.Equals(".") == false)
                {
                    _parentDirectoryNameMap[parentKey].Add(new ParentMapEntry(fna.FileInfo.FileName,
                        fileRecord.Value.GetKey(true),
                        fileRecord.Value.IsDirectory()));
                }
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

                var key = fileRecord.GetKey();
                var parentKey = fna.FileInfo.ParentMftRecord.GetKey();

                if (_parentDirectoryNameMap.ContainsKey(parentKey) == false)
                {
                    _parentDirectoryNameMap.Add(parentKey, new HashSet<ParentMapEntry>());
                }

                if (fna.FileInfo.FileName.Equals(".") == false)
                {
                    _parentDirectoryNameMap[parentKey].Add(new ParentMapEntry(fna.FileInfo.FileName,
                        fileRecord.GetKey(true),
                        fileRecord.IsDirectory()));
                }
            }
        }
    }

    public List<ParentMapEntry> GetDirectoryContents(string key)
    {
        if (_parentDirectoryNameMap.ContainsKey(key))
        {
            return _parentDirectoryNameMap[key].OrderByDescending(t => t.IsDirectory).ThenBy(t => t.FileName).ToList();
        }

        return new List<ParentMapEntry>();
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

        while (_directoryNameMap.ContainsKey(tempKey))
        {
            var dir = _directoryNameMap[tempKey];
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