using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using MFT.Attributes;
using MFT.Other;
using NLog;

namespace MFT
{
    public class Mft
    {
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

            var rootFolder = FileRecords.Single(t => t.Value.EntryNumber == 5).Value;

            RootDirectory = new DirectoryItem("", rootFolder.Key(), ".", false, null, rootFolder.GetFileSize(), false,
                false);

        }
        
        public DirectoryItem RootDirectory { get; }

        public Dictionary<string, FileRecord> FileRecords { get; }
        public Dictionary<string, FileRecord> FreeFileRecords { get; }

        public List<FileRecord> BadRecords { get; }
        public List<FileRecord> UninitializedRecords { get; }

        private readonly Dictionary<string, DirectoryNameMapValue> _directoryNameMap;

        internal class DirectoryNameMapValue
        {
            public DirectoryNameMapValue(string name, string parentRecordKey, bool isDeleted)
            {
                Name = name;
                IsDeleted = isDeleted;
                ParentRecordKey = parentRecordKey;
            }

            public string Name { get; }
            public string ParentRecordKey { get; }
            public bool IsDeleted { get; }

            public override string ToString()
            {
                return $"{Name}, Parent key: {ParentRecordKey} Deleted: {IsDeleted}";
            }
        }

        /// <summary>
        /// Given an MFT entry # and seq #, return the full path
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

     

//        private void BuildRootDirFromRecords(bool includeShortNames, Dictionary<string, FileRecord> records)
//        {
//            foreach (var fileRecord in records)
//            {
//                if (fileRecord.Value.GetFileNameAttributeFromFileRecord() == null)
//                {
//                    _logger.Debug(
//                        $"Skipping record at offset 0x{fileRecord.Value.Offset:X} because it has no $FILE_NAME attributes");
//                    continue;
//                }
//
//                if (fileRecord.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
//                    fileRecord.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
//                {
//                    //will get this record via attributeList
//                    _logger.Debug(
//                        $"Skipping record at offset 0x{fileRecord.Value.Offset:X} because it is an extension record");
//                    continue;
//                }
//
//                if (fileRecord.Value.IsDirectory() == false)
//                {
//                    var baseEntryNumber = -1;
//                    foreach (var attribute in fileRecord.Value.Attributes.Where(t =>
//                        t.AttributeType == AttributeType.FileName))
//                    {
//                        var map = GetMap(fileRecord.Value, attribute.AttributeNumber);
//                        var path = GetFullPathFromMap(map);
//                        var fna1 = (FileName) attribute;
//
//                        if ((includeShortNames == false) & (fna1.FileInfo.NameType == NameTypes.Dos))
//                        {
//                            continue;
//                        }
//
//                        if (baseEntryNumber == -1)
//                        {
//                            baseEntryNumber = (int) fna1.FileInfo.ParentMftRecord.MftEntryNumber;
//                        }
//
//                        var isHardLink = false;
//                        isHardLink = fna1.FileInfo.ParentMftRecord.MftEntryNumber != baseEntryNumber;
//
//                        var dirItem = UpdateDirectoryItems(path, map, isHardLink, fileRecord.Value.IsDeleted());
//                        var fna = (FileName) attribute;
//
//                        //add fna to dirItem
//
//                        var parentPath = path;// $"{dirItem.ParentPath}\\{dirItem.Name}";
//
//                        var fkey =
//                            $"{fileRecord.Value.Key()}-{fna.AttributeNumber:X8}";
//                        var fItem = new DirectoryItem(fna.FileInfo.FileName, fkey,
//                            parentPath,
//                            fileRecord.Value.HasAds(), null, fileRecord.Value.GetFileSize(), isHardLink,
//                            fileRecord.Value.IsDeleted());
//
//                        dirItem.SubItems.Add(fkey, fItem);
//
//                        _logger.Trace(
//                            $"TEST: {fna.FileInfo.FileName} with key {fileRecord.Value.Key()} ==> {path} File size: 0x{fileRecord.Value.GetFileSize():X}");
//                    }
//                }
//            }
//        }
//
//        private DirectoryItem UpdateDirectoryItems(string path, string map, bool isHardLink, bool isDeleted)
//        {
//            var startDirectory = RootDirectory;
//
//            if (startDirectory.ParentPath == path)
//            {
//                return startDirectory;
//            }
//
//            var pathSegs = path.Split(new[] {'\\'}, StringSplitOptions.RemoveEmptyEntries);
//            var mapSegs = map.Split(new[] {'\\'}, StringSplitOptions.RemoveEmptyEntries);
//
//            var parentPath = ".";
//
//            for (var i = 0; i < mapSegs.Length; i++)
//            {
//                if (startDirectory.SubItems.ContainsKey(mapSegs[i]))
//                {
//                    //its already there
//                    startDirectory = startDirectory.SubItems[mapSegs[i]];
//
//                    if (mapSegs[i] != "PathUnknown" && GetFileRecord(mapSegs[i]).IsDirectory())
//                    {
//                        
//                        parentPath = $"{parentPath}\\{startDirectory.Name}";
//                    }
//                    else
//                    {
//                        parentPath = $"{parentPath}\\{mapSegs[i]}";
//                    }
//                }
//                else
//                {
//                    if (mapSegs[i] == "PathUnknown")
//                    {
//                        var pun = new DirectoryItem("PathUnknown", mapSegs[i], parentPath, false, null, 0, false, true);
//                        startDirectory.SubItems.Add(mapSegs[i], pun);
//
//                        startDirectory = startDirectory.SubItems[mapSegs[i]];
//                        continue;
//                    }
//
//                    //we need to add it
//                    var fileRecord = GetFileRecord(mapSegs[i]);
//
//                    if (fileRecord == null)
//                    {
//                        var pun = new DirectoryItem($"Directory with ID 0x{mapSegs[i]}", mapSegs[i],
//                            $"{startDirectory.ParentPath}\\{startDirectory.Name}", false, null, 0, false,
//                          true);
//                        if (startDirectory.SubItems.ContainsKey(mapSegs[i]) == false)
//                        {
//                            startDirectory.SubItems.Add(mapSegs[i], pun);
//                        }
//
//                        startDirectory = startDirectory.SubItems[mapSegs[i]];
//                        continue;
//                    }
//
//                    parentPath = string.Join("\\", pathSegs.Take(i + 1));
//
//                    var name = fileRecord.GetFileNameAttributeFromFileRecord().FileInfo.FileName;
//                    var fsize = fileRecord.GetFileSize();
//                    var hasAds = fileRecord.HasAds();
//                    var reparse = fileRecord.GetReparsePoint();
//                    if (fileRecord.IsDirectory() == false)
//                    {
//                        name = mapSegs.Last();
//                        fsize = 0;
//                        hasAds = false;
//                        reparse = null;
//                    }
//
//                    var newItem = new DirectoryItem(name,
//                        mapSegs[i], parentPath, hasAds, reparse,
//                        fsize, isHardLink, isDeleted);
//                    startDirectory.SubItems.Add(mapSegs[i], newItem);
//
//                    startDirectory = startDirectory.SubItems[mapSegs[i]];
//                }
//            }
//
//            return startDirectory;
//        }
//
//        private string GetMap(FileRecord fileRecord, int attributeNumber = -1)
//        {
//            string map;
//            string key;
//            if (fileRecord.IsDirectory() == false)
//            {
//                FileName fr = null;
//
//                if (attributeNumber == -1)
//                {
//                    fr = fileRecord.GetFileNameAttributeFromFileRecord();
//                }
//                else
//                {
//                    fr = (FileName) fileRecord.Attributes.Single(t => t.AttributeNumber == attributeNumber);
//                }
//
//                if (fr == null)
//                {
//                    return "(NONE)";
//                }
//
//                key =
//                    $"{fr.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fr.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";
//
//                if (_directoryPathMap.ContainsKey(key) && GetFileRecord(key).IsDirectory())
//                {
//                    map = _directoryPathMap[key];
//                }
//                else
//                {
//                    map = _directoryPathMap[fileRecord.Key()];
//                    return map;
//                }
//
//                //this is a file, so add its key to get the full path for the file, since key in this case is where the file actually lives
//                if (key != "00000005-00000005")
//                {
//                    map = $"{map}\\{key}";
//                }
//            }
//            else
//            {
//                //this is a directory
//                key = fileRecord.Key();
//                map = _directoryPathMap[key];
//            }
//
//            return map;
//        }
//
//        private string GetFullPathFromMap(string map)
//        {
//            if (map == "(NONE)")
//            {
//                return "(None)";
//            }
//
//            var segs = map.Split(new[] {'\\'}, StringSplitOptions.RemoveEmptyEntries).ToList();
//
//            var path = new List<string>
//            {
//                "."
//            };
//
//            foreach (var seg in segs)
//            {
//                if (seg == ".")
//                {
//                    continue;
//                }
//
//                if (seg == "PathUnknown")
//                {
//                    path.Clear();
//                    path.Add(seg);
//                    continue;
//                }
//
//                var foo = GetFileRecord(seg);
//
//                if (foo == null)
//                {
//                    path.Add($"Directory with ID 0x{seg}");
//                }
//                else
//                {
//                    if (foo.IsDirectory())
//                    {
//                        path.Add(foo.GetFileNameAttributeFromFileRecord().FileInfo.FileName);
//                    }
//                    else
//                    {
//                        path.Clear();
//                        //path.Add(".");
//                        path.Add("PathUnknown");
//                        path.Add($"Directory with ID 0x{seg}");
//                    }
//                }
//            }
//
//            return string.Join("\\", path);
//        }
//
//        public FileRecord GetFileRecord(string key)
//        {
//            if (key == "(NONE)" || key == "PathUnknown")
//            {
//                return null;
//            }
//
//            var segs = key.Split('-');
//            if (segs.Length == 3)
//            {
//                //this is for a file, so adjust
//                key = $"{segs[0]}-{segs[1]}";
//            }
//
//
//
//            if (FileRecords.ContainsKey(key))
//            {
//                return FileRecords[key];
//            }
//
//            if (FreeFileRecords.ContainsKey(key))
//            {
//                return FreeFileRecords[key];
//            }
//
//           
//            var entry = int.Parse(segs[0], NumberStyles.HexNumber);
//            var seq = int.Parse(segs[1], NumberStyles.HexNumber);
//
//            seq += 1;
//
//            var newKey = $"{entry:X8}-{seq:X8}";
//
//            if (FreeFileRecords.ContainsKey(newKey))
//            {
//                return FreeFileRecords[newKey];
//            }
//
//            return null;
//        }

        /// <summary>
        /// Creates a map for directories and their parent directories. 
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
                                    $"Cannot find record with entry/seq #: 0x{attrEntryKey} Deleted: {fileRecord.Value.IsDeleted()}");
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

//                var reparseAttr =
//                    fileRecord.Value.Attributes.Where(t =>
//                        t.AttributeType == AttributeType.ReparsePoint).ToList();
//
//                var reparsePoint = (ReparsePoint) reparseAttr.FirstOrDefault();
//
//                if (reparsePoint != null)
//                {
//                    _logger.Trace($"Found reparse point: {reparsePoint.PrintName} --> {reparsePoint.SubstituteName}");
//                }

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
                        _directoryNameMap.Add(key,new DirectoryNameMapValue(fna.FileInfo.FileName,$"{fna.FileInfo.ParentMftRecord.GetKey()}",fileRecord.Value.IsDeleted()));    
                    }
                    
                }
            }
        }

    }
}