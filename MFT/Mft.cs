using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using MFT.Attributes;
using MFT.Other;
using NLog;

namespace MFT
{
    public class Mft
    {
        private readonly Dictionary<string, DirectoryNameMapValue> _directoryNameMap;
        private readonly Logger _logger = LogManager.GetLogger("MFT");

        public Mft(byte[] rawbytes)
        {
            FileRecords = new Dictionary<string, FileRecord>();
            FreeFileRecords = new Dictionary<string, FileRecord>();
            ExtensionFileRecords = new Dictionary<string, List<FileRecord>>();
            BadRecords = new List<FileRecord>();
            UninitializedRecords = new List<FileRecord>();

            const int blockSize = 1024;

            var fileBytes = new byte[blockSize];

            var index = 0;

            while (index < rawbytes.Length)
            {
                Buffer.BlockCopy(rawbytes, index, fileBytes, 0, blockSize);

                var f = new FileRecord(fileBytes, index);

                var key = f.GetKey();

                _logger.Trace($"Offset: 0x{f.Offset:X} flags: {f.EntryFlags} key: {key}");

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
                    FreeFileRecords.Add(key, f);
                }
                else
                {
                    FileRecords.Add(key, f);
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

            ProcessExtensionBlocks();

            BuildDirectoryNameMap(FileRecords.Where(t => t.Value.IsDirectory()));
            BuildDirectoryNameMap(FreeFileRecords.Where(t => t.Value.IsDirectory()));
        }

        private void ProcessExtensionBlocks()
        {
                foreach (var fileRecord in ExtensionFileRecords)
                {
                    FileRecord baseRecord=null;
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

                    //pull in all related attributes from this record for processing later
                    foreach (var fileRecordAttribute in fileRecord.Value)
                    {
                        baseRecord.Attributes.AddRange(fileRecordAttribute.Attributes);    
                    }
                }
        }

        public Dictionary<string, FileRecord> FileRecords { get; }
        private Dictionary<string, List<FileRecord>> ExtensionFileRecords { get; }
        public Dictionary<string, FileRecord> FreeFileRecords { get; }

        public List<FileRecord> BadRecords { get; }
        public List<FileRecord> UninitializedRecords { get; }

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