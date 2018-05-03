using System;
using System.Collections.Generic;
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


        private HashSet<string> ProcesssedFileRecords = new HashSet<string>();

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
                else if ((f.EntryFlags & FileRecord.EntryFlag.FileRecordSegmentInUse) !=
                         FileRecord.EntryFlag.FileRecordSegmentInUse)
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

        public void BuildFileSystem()
        {
            //For all directories, build out a map where key == parent directrory id and value is how to get there
            BuildDirectoryPathMap(FileRecords.Where(t => t.Value.IsDirectory()));
            BuildDirectoryPathMap(FreeFileRecords.Where(t => t.Value.IsDirectory()));

            //process free files to check for whether the map contains a reference to its parent directory
            BuildDirectoryPathMap(FreeFileRecords.Where(t => t.Value.IsDirectory() == false));


            //at this point, _directoryPathMap contains a reference for all possible directories
            //iterate in use and free files and build directory structure

            var key = string.Empty;
            FileRecord fr = null;
            var map = string.Empty;
            var path = string.Empty;

            foreach (var fileRecord in FileRecords)
            {
                key = fileRecord.Value.Key();
                fr = GetFileRecord(key);

                if (fr.GetFileNameAttributeFromFileRecord() == null)
                {
                    _logger.Info(
                        $"Skipping in use filerecord at offset 0x{fileRecord.Value.Offset} because it has no file_name attributes");
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
                        var fna = (FileName) attribute;

                        _logger.Info(
                            $"TEST DELETED: {fna.FileInfo.FileName} with key {key} ==> {path} File size: 0x{fileRecord1.Value.GetFileSize():X}");
                    }
                }
            }


//              //XWF tests
//            //file test, existing
//            key = "0000005F-00000002"; //\Documents and Settings\EdgarAllanPoe\My Documents\My Pictures\smallpic.jpg
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//
//            //file test, deleted 
//            key = "00000270-00000001"; //\Documents and Settings\EdgarAllanPoe\My Documents\My Pictures\Dog.gif
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//
//            //file test, existing //0000004D-00000002 == Trash
//            key = "0000004D-00000002"; //\Documents and Settings\EdgarAllanPoe\My Documents\Trash
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//            
//            //dir test, existing
//            key = "00000196-00000003"; //\Docs\Pictures
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//            
//            //dir test, deleted
//            
//            key = "0000021C-00000001"; //\Documents and Settings\EdgarAllanPoe\Local Settings\Temp\Temporary Internet Files\Content.IE5\M3ILGGNU
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//           
//
//
//            key = "0000F8B3-00000001"; //\Documents and Settings\EdgarAllanPoe\Local Settings\Temp\Temporary Internet Files\Content.IE5\M3ILGGNU
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");

/*            //tdungan
            key = "0009322-00000003"; //\Program Files\Mozilla Firefox\extensions\{CAFEEFAC-0016-0000-0031-ABCDEFFEDCBA}\chrome\content\ffjcext\ffjcext.xul
            fr = GetFileRecord(key);
            map = GetMap(fr);
            path = GetFullPathFromMap(map);
            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");

            key = "00006E5E-00000004"; //\Program Files\Mozilla Firefox\extensions\{CAFEEFAC-0016-0000-0031-ABCDEFFEDCBA}\chrome\content\ffjcext\ffjcext.xul
            fr = GetFileRecord(key);
            map = GetMap(fr);
            path = GetFullPathFromMap(map);
            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");

            key = "00009341-00000004"; //\Program Files\Mozilla Firefox\extensions\{CAFEEFAC-0016-0000-0031-ABCDEFFEDCBA}\chrome\locale\zh-TW\ffjcext
            fr = GetFileRecord(key);
            map = GetMap(fr);
            path = GetFullPathFromMap(map);
            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");


            key = "00006214-00000001"; //\Program Files\Microsoft Silverlight\4.0.60531.0\pt-BR\mscorrc.dll
            fr = GetFileRecord(key);
            map = GetMap(fr);
            path = GetFullPathFromMap(map);
            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");*/
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

            var path = new List<string>();

            path.Add(".");

            if (map.Contains("Unknown"))
            {
                segs.Reverse();
            }

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
                    path.Add(foo.GetFileNameAttributeFromFileRecord().FileInfo.FileName);
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
                    _logger.Warn($"File record at offset 0x{fileRecord.Value.Offset:X} has no attributes. Skipping");
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
                                _logger.Warn($"Cannot find record with entry/seq #: 0x{attrEntryKey}");
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

                var path = GetParentPathFromInUse(fna);

                _directoryPathMap.Add(fileRecord.Value.Key(), path);

                //     _logger.Info($"key: {fileRecord.Value.Key()} {fna.FileInfo.FileName} (is dir: {fileRecord.Value.IsDirectory()} deleted: {fileRecord.Value.IsDeleted()})> {fileRecord.Value.Key()} ==> {path}");
            }
        }

        private string GetParentPathFromInUse(FileName fileName)
        {
            var parentKey =
                $"{fileName.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fileName.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";

            var path = ".";

            while (parentKey != RootDirectory.Key)
            {
                //traverse up the chain

                path = $"{parentKey}\\{path}";

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
                        //path = path.Replace(".", _directoryPathMap[parentKey]);

                        path = $"{_directoryPathMap[parentKey].Replace("\\.", "")}\\{path}";

                        return path;
                    }

                    path = path.Replace(".", "PathUnknown\\.");

                    return path;
                }

                var parentFn = parentRecord.GetFileNameAttributeFromFileRecord();

                parentKey =
                    $"{parentFn.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{parentFn.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";
            }

            return path;
        }


//   
    }
}