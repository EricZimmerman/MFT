using System;
using System.Collections.Generic;
using System.Linq;
using MFT.Attributes;
using MFT.Other;
using NLog;

namespace MFT
{
    public class Mft
    {
        private readonly Dictionary<string, DirectoryNameMapValue> _directoryNameMap;
        private readonly Logger _logger = LogManager.GetLogger("MFT");
        private readonly Dictionary<string, HashSet<ParentMapEntry>> _parentDirectoryNameMap;

        public Mft(byte[] rawBytes)
        {
            FileRecords = new Dictionary<string, FileRecord>();
            FreeFileRecords = new Dictionary<string, FileRecord>();
            ExtensionFileRecords = new Dictionary<string, List<FileRecord>>();
            BadRecords = new List<FileRecord>();
            UninitializedRecords = new List<FileRecord>();

            var sig = BitConverter.ToInt32(rawBytes, 0);
            if (sig != 0x454c4946) //Does not match FILE
            {
                throw new Exception("Invalid header! Expected 'FILE' Signature.");
            }

            var blockSize = BitConverter.ToInt32(rawBytes, 0x1c);

            var fileBytes = new byte[blockSize];

            var index = 0;

            while (index < rawBytes.Length)
            {
                Buffer.BlockCopy(rawBytes, index, fileBytes, 0, blockSize);

                CurrentOffset = index;

                var f = new FileRecord(fileBytes, index);

                var key = f.GetKey();

                _logger.Trace($"Offset: 0x{f.Offset:X} flags: {f.EntryFlags.ToString().Replace(", ", "|")} key: {key}");

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
                        _logger.Warn(
                            $"At offset 0x{f.Offset:X}, a free FILE record with key '{key}' already exists! You may want to review this manually. Skipping...");
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
                        _logger.Warn(
                            $"At offset 0x{f.Offset:X}, a FILE record with key '{key}' already exists! You may want to review this manually. Skipping...");
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
            BuildMaps(FreeFileRecords);
        }


        public Dictionary<string, FileRecord> FileRecords { get; }
        private Dictionary<string, List<FileRecord>> ExtensionFileRecords { get; }
        public Dictionary<string, FileRecord> FreeFileRecords { get; }

        public List<FileRecord> BadRecords { get; }
        public List<FileRecord> UninitializedRecords { get; }

        /// <summary>
        ///     When the MFT is being processed, this is set to the offset where the FILE record being processed starts.
        ///     <remarks>Used to include the offset where errors happen in parsing for log messages</remarks>
        /// </summary>
        public static int CurrentOffset { get; private set; }

        private void BuildMaps(Dictionary<string, FileRecord> fileRecords)
        {
            foreach (var fileRecord in fileRecords)
            {
                if (fileRecord.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                    fileRecord.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
                {
                    //will get this record via extensionRecord
                    continue;
                }

                if (fileRecord.Value.Attributes.Count == 0)
                {
                    _logger.Debug($"Skipping file record at offset 0x{fileRecord.Value.Offset:X} has no attributes");
                    continue;
                }

                var fileNameRecords = fileRecord.Value.Attributes.Where(t => t.AttributeType == AttributeType.FileName)
                    .ToList();

                foreach (var fileNameRecord in fileNameRecords)
                {
                    var fna = (FileName) fileNameRecord;
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
                    else
                    {
                        var key = fileRecord.Value.GetKey();
                        var parentKey = fna.FileInfo.ParentMftRecord.GetKey();

                        if (_parentDirectoryNameMap.ContainsKey(parentKey) == false)
                        {
                            _parentDirectoryNameMap.Add(parentKey, new HashSet<ParentMapEntry>());
                        }

                        if (fna.FileInfo.FileName.Equals(".") == false)
                        {
                            _parentDirectoryNameMap[parentKey].Add(new ParentMapEntry(fna.FileInfo.FileName, key,
                                fileRecord.Value.IsDirectory()));
                        }
                    }
                }
            }
        }

        public List<ParentMapEntry> GetDirectoryContents(string key)
        {
            if (_parentDirectoryNameMap.ContainsKey(key))
            {
                return _parentDirectoryNameMap[key].OrderByDescending(t=>t.IsDirectory).ThenBy(t=>t.FileName).ToList();
            }

            return new List<ParentMapEntry>();
        }

        private void ProcessExtensionBlocks()
        {
            _logger.Debug("Processing Extension FILE records");

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
                    continue;
                }

                _logger.Debug($"FILE record '{fileRecord.Key}', Extension records found: {fileRecord.Value.Count:N0}");

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
                {
                    //all done since we are at root
                    break;
                }

                tempKey = dir.ParentRecordKey;
            }

            if (tempKey != "00000005-00000005")
            {
                //we dropped out of our map too early, so adjust it
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
                {
                    //will get this record via extensionRecord
                    continue;
                }

                if (fileRecord.Value.Attributes.Count == 0)
                {
                    _logger.Debug($"Skipping file record at offset 0x{fileRecord.Value.Offset:X} has no attributes");
                    continue;
                }

                var fileNameRecords = fileRecord.Value.Attributes.Where(t => t.AttributeType == AttributeType.FileName)
                    .ToList();

                foreach (var fileNameRecord in fileNameRecords)
                {
                    var fna = (FileName) fileNameRecord;
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
        }
    }
}