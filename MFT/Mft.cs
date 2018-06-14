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
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public Mft(byte[] rawbytes)
        {
            FileRecords = new Dictionary<string, FileRecord>();
            FreeFileRecords = new Dictionary<string, FileRecord>();
            BadRecords = new List<FileRecord>();
            UninitializedRecords = new List<FileRecord>();

            const int blockSize = 1024;

            var fileBytes = new byte[1024];

            var index = 0;

            while (index < rawbytes.Length)
            {
                Buffer.BlockCopy(rawbytes, index, fileBytes, 0, blockSize);

                var f = new FileRecord(fileBytes, index);

                var key = f.GetKey();

                _logger.Debug($"offset: 0x{f.Offset:X} flags: {f.EntryFlags} key: {key}");

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

                index += blockSize;
            }

            _directoryNameMap = new Dictionary<string, DirectoryNameMapValue>();

            BuildDirectoryNameMap(FileRecords.Where(t => t.Value.IsDirectory()));
            BuildDirectoryNameMap(FreeFileRecords.Where(t => t.Value.IsDirectory()));
        }

        public Dictionary<string, FileRecord> FileRecords { get; }
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
                    //will get this record via attributeList
                    //TODO verify this case.
                    continue;
                }

                if (fileRecord.Value.Attributes.Count == 0)
                {
                    _logger.Debug($"Skipping file record at offset 0x{fileRecord.Value.Offset:X} has no attributes");
                    continue;
                }

                //look for attribute list, pull out non-self referencing attributes
                var attrList =
                    (AttributeList) fileRecord.Value.Attributes.SingleOrDefault(t =>
                        t.AttributeType == AttributeType.AttributeList);

                if (attrList != null)
                {
                    foreach (var attrListAttributeInformation in attrList.AttributeInformations)
                    {
                        if (attrListAttributeInformation.EntryInfo.MftEntryNumber != fileRecord.Value.EntryNumber &&
                            attrListAttributeInformation.EntryInfo.MftSequenceNumber != fileRecord.Value.SequenceNumber)
                        {
                            _logger.Trace($"found attrlist item: {attrListAttributeInformation}");

                            var attrEntryKey =
                                $"{attrListAttributeInformation.EntryInfo.MftEntryNumber:X8}-{attrListAttributeInformation.EntryInfo.MftSequenceNumber:X8}";

                            if (FileRecords.ContainsKey(attrEntryKey) == false)
                            {
                                _logger.Warn(
                                    $"Cannot find record with entry/seq #: 0x{attrEntryKey} from Attribute list. Deleted: {fileRecord.Value.IsDeleted()}");
                            }
                            else
                            {
                                var attrEntry = FileRecords[attrEntryKey];

                                //pull in all related attributes from this record for processing later
                                fileRecord.Value.Attributes.AddRange(attrEntry.Attributes);
                            }
                        }
                    }
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