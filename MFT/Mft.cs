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
        private readonly Dictionary<string, string> _directoryPathMap;
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

                var key = $"{f.EntryNumber:X8}-{f.SequenceNumber:X8}";

                _logger.Debug($"offset: 0x{f.Offset:X} flags: {f.EntryFlags} key: {key}");

                if (f.IsBad)
                {
                    BadRecords.Add(f);
                }
                else if (f.IsUninitialized)
                {
                    UninitializedRecords.Add(f);
                }
                else if ((f.EntryFlags & FileRecord.EntryFlag.InUse) !=
                         FileRecord.EntryFlag.InUse)
                {
                    FreeFileRecords.Add(key, f);
                }
                else
                {
                    FileRecords.Add(key, f);
                }

                index += blockSize;
            }

            //this will keep track of the path where each entry can be found
            //key == entry #-seq #
            //value is path to a given key == .\entry-seq\entry-seq and so on from root
            _directoryPathMap = new Dictionary<string, string>();

            var rootFolder = FileRecords.Single(t => t.Value.EntryNumber == 5).Value;

            RootDirectory = new DirectoryItem("", rootFolder.Key(), ".", false, null, rootFolder.GetFileSize(), false,
                false);

            _directoryPathMap.Add(rootFolder.Key(), ".");
        }

        public DirectoryItem RootDirectory { get; }

        public Dictionary<string, FileRecord> FileRecords { get; }
        public Dictionary<string, FileRecord> FreeFileRecords { get; }

        public List<FileRecord> BadRecords { get; }
        public List<FileRecord> UninitializedRecords { get; }

        public void BuildFileSystem(bool includeShortNames = false)
        {
            //For all directories, build out a map where key == parent directrory id and value is how to get there
            BuildDirectoryPathMap(FileRecords.Where(t => t.Value.IsDirectory()));
            BuildDirectoryPathMap(FreeFileRecords.Where(t => t.Value.IsDirectory()));

            //process free files to check for whether the map contains a reference to its parent directory
            BuildDirectoryPathMap(FreeFileRecords.Where(t => t.Value.IsDirectory() == false));

            //at this point, _directoryPathMap contains a reference for all possible directories
            //iterate in use and free files and build directory structure

            //this is where we need to build sub items from RootDirectory

            var key = string.Empty;
            FileRecord fr = null;
            var map = string.Empty;
            var path = string.Empty;

            foreach (var fileRecord in FileRecords)
            {
                key = string.Empty;
                fr = null;
                map = string.Empty;
                path = string.Empty;

                key = fileRecord.Value.Key();
                fr = GetFileRecord(key);

                if (fr.GetFileNameAttributeFromFileRecord() == null)
                {
                    _logger.Info(
                        $"Skipping in use filerecord at offset 0x{fileRecord.Value.Offset} because it has no $FILE_NAME attributes");
                    continue;
                }

                if (fileRecord.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                    fileRecord.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
                {
                    //will get this record via attributeList
                    _logger.Info(
                        $"Skipping in use filerecord at offset 0x{fileRecord.Value.Offset} because it is an extension record");
                    continue;
                }

                if (fr.IsDirectory())
                {
                    map = GetMap(fr);
                    path = GetFullPathFromMap(map);
                    var fn = fr.GetFileNameAttributeFromFileRecord();
                    _logger.Info($"TEST: {fn?.FileInfo.FileName} with key {key} ==> {path}");
                }
                else
                {
                    foreach (var attribute in fr.Attributes.Where(t => t.AttributeType == AttributeType.FileName))
                    {
                        map = GetMap(fr, attribute.AttributeNumber);
                        path = GetFullPathFromMap(map);
                        var fna = (FileName) attribute;

                        _logger.Info(
                            $"TEST: {fna.FileInfo.FileName} with key {key} ==> {path} File size: 0x{fileRecord.Value.GetFileSize():X}");
                    }
                }
            }

            foreach (var fileRecord1 in FreeFileRecords)
            {
                key = string.Empty;
                fr = null;
                map = string.Empty;
                path = string.Empty;

                key = fileRecord1.Value.Key();
                fr = GetFileRecord(key);

                if (fileRecord1.Value.Attributes.Count == 0)
                {
                    _logger.Info(
                        $"Skipping free filerecord at offset 0x{fileRecord1.Value.Offset} because it has no attributes");
                    continue;
                }

                if (fr.GetFileNameAttributeFromFileRecord() == null)
                {
                    _logger.Info(
                        $"Skipping free filerecord at offset 0x{fileRecord1.Value.Offset} because it has no file_name attributes");
                    continue;
                }

                if (fileRecord1.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                    fileRecord1.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
                {
                    //will get this record via attributeList
                    _logger.Info(
                        $"Skipping free filerecord at offset 0x{fileRecord1.Value.Offset} because it is an extension record");
                    continue;
                }

                if (fr.IsDirectory())
                {
                    map = GetMap(fr);
                    path = GetFullPathFromMap(map);
                    var fn = fr.GetFileNameAttributeFromFileRecord();
                    _logger.Info($"TEST DELETED: {fn?.FileInfo.FileName} with key {key} ==> {path}");
                }
                else
                {
                    foreach (var attribute in fr.Attributes.Where(t => t.AttributeType == AttributeType.FileName))
                    {
                        map = GetMap(fr, attribute.AttributeNumber);
                        path = GetFullPathFromMap(map);
                        var fna1 = (FileName) attribute;

                        if (fna1.FileInfo.FileName.Contains("5.tmp"))
                        {
                            Debug.WriteLine(1);
                        }

                        _logger.Info(
                            $"TEST DELETED: {fna1.FileInfo.FileName} with key {key} ==> {path} File size: 0x{fileRecord1.Value.GetFileSize():X}");
                    }
                }
            }
        }

        private string GetMap(FileRecord fileRecord, int attributeNumber = -1)
        {
            string map;
            string key;
            if (fileRecord.IsDirectory() == false)
            {
                FileName fr = null;

                if (attributeNumber == -1)
                {
                    fr = fileRecord.GetFileNameAttributeFromFileRecord();
                }
                else
                {
                    fr = (FileName) fileRecord.Attributes.Single(t => t.AttributeNumber == attributeNumber);
                }

                if (fr == null)
                {
                    return "(NONE)";
                }

                key =
                    $"{fr.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fr.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";

                if (_directoryPathMap.ContainsKey(key))
                {
                    map = _directoryPathMap[key];
                }
                else
                {
                    map = _directoryPathMap[fileRecord.Key()];
                    return map;
                }

                //this is a file, so add its key to get the full path for the file, since key in this case is where the file actually lives
                if (key != "00000005-00000005")
                {
                    map = $"{map}\\{key}";
                }
            }
            else
            {
                //this is a directory
                key = fileRecord.Key();
                map = _directoryPathMap[key];
            }

            return map;
        }

        private string GetFullPathFromMap(string map)
        {
            if (map == "(NONE)")
            {
                return "(None)";
            }

            var segs = map.Split(new[] {'\\'}, StringSplitOptions.RemoveEmptyEntries).ToList();

            var path = new List<string>
            {
                "."
            };

            foreach (var seg in segs)
            {
                if (seg == ".")
                {
                    continue;
                }

                if (seg == "PathUnknown")
                {
                    path.Add(seg);
                    continue;
                }

                var foo = GetFileRecord(seg);

                if (foo == null)
                {
                    path.Add($"Directory with ID 0x{seg}");
                }
                else
                {
                    if (foo.IsDirectory())
                    {
                        path.Add(foo.GetFileNameAttributeFromFileRecord().FileInfo.FileName);
                    }
                    else
                    {
                        path.Clear();
                        path.Add(".");
                        path.Add("PathUnknown");
                        path.Add($"Directory with ID 0x{seg}");
                    }
                }
            }

            return string.Join("\\", path);
        }

        private FileRecord GetFileRecord(string key)
        {
            if (key == "(NONE)")
            {
                return null;
            }

            if (FileRecords.ContainsKey(key))
            {
                return FileRecords[key];
            }

            if (FreeFileRecords.ContainsKey(key))
            {
                return FreeFileRecords[key];
            }

            var segs = key.Split('-');
            var entry = int.Parse(segs[0], NumberStyles.HexNumber);
            var seq = int.Parse(segs[1], NumberStyles.HexNumber);

            seq += 1;

            var newKey = $"{entry:X8}-{seq:X8}";

            if (FreeFileRecords.ContainsKey(newKey))
            {
                return FreeFileRecords[newKey];
            }

            return null;
        }

        private void BuildDirectoryPathMap(IEnumerable<KeyValuePair<string, FileRecord>> fileRecords)
        {
            foreach (var fileRecord in fileRecords)
            {
                if (fileRecord.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                    fileRecord.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
                {
                    //will get this record via attributeList
                    continue;
                }

                if (RootDirectory.Key == fileRecord.Key)
                {
                    //skip entry 5
                    continue;
                }

                if (fileRecord.Value.Attributes.Count == 0)
                {
                    _logger.Warn($"Skipping file record at offset 0x{fileRecord.Value.Offset:X} has no attributes");
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

                var reparseAttr =
                    fileRecord.Value.Attributes.Where(t =>
                        t.AttributeType == AttributeType.ReparsePoint).ToList();

                var reparsePoint = (ReparsePoint) reparseAttr.FirstOrDefault();

                if (reparsePoint != null)
                {
                    _logger.Trace($"Found reparse point: {reparsePoint.PrintName} --> {reparsePoint.SubstituteName}");
                }

                var fileNameRecords = fileRecord.Value.Attributes.Where(t => t.AttributeType == AttributeType.FileName)
                    .ToList();

                FileName fna;

                if (fileNameRecords.Count == 1)
                {
                    fna = (FileName) fileNameRecords.First();
                }
                else
                {
                    fna = fileRecord.Value.GetFileNameAttributeFromFileRecord();
                }

                var path = GetParentPath(fna);

                _directoryPathMap.Add(fileRecord.Value.Key(), path);

                     _logger.Trace($"key: {fileRecord.Value.Key()} {fna.FileInfo.FileName} (is dir: {fileRecord.Value.IsDirectory()} deleted: {fileRecord.Value.IsDeleted()})> {fileRecord.Value.Key()} ==> {path}");
            }
        }

        private string GetParentPath(FileName fileName)
        {
            var parentKey =
                $"{fileName.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fileName.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";

            var stack = new Stack<string>();

            stack.Push(".");

            while (parentKey != RootDirectory.Key)
            {
                //traverse up the chain
                stack.Push(parentKey);

                FileRecord parentRecord = null;

                if (FileRecords.ContainsKey(parentKey) || FreeFileRecords.ContainsKey(parentKey))
                {
                    //it exists somewhere
                    if (FileRecords.ContainsKey(parentKey))
                    {
                        parentRecord = FileRecords[parentKey];
                    }
                    else
                    {
                        parentRecord = FreeFileRecords[parentKey];
                    }
                }
                else
                {
                    //this entries parent doesnt exist any more, so make it show up under "PathUnknown", unless we already know where it goes based on DirectoryPathMap
                    if (_directoryPathMap.ContainsKey(parentKey))
                    {
                        stack.Push(_directoryPathMap[parentKey].Replace("\\.", ""));

                        return string.Join("\\", stack);
                    }

                    return $".\\PathUnknown\\{parentKey}";
                }

                var parentFn = parentRecord.GetFileNameAttributeFromFileRecord();

                parentKey =
                    $"{parentFn.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{parentFn.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";
            }

            return string.Join("\\", stack);
        }
    }
}