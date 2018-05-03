using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                else if ((f.EntryFlags & FileRecord.EntryFlag.FileRecordSegmentInUse) != FileRecord.EntryFlag.FileRecordSegmentInUse)
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
            
            RootDirectory = new DirectoryItem("", rootFolder.Key(), ".",false,null,rootFolder.GetFileSize(),false,false);

            _directoryPathMap.Add(rootFolder.Key(),".");
        }

        public DirectoryItem RootDirectory { get; }

        public Dictionary<string, FileRecord> FileRecords { get; }
        public Dictionary<string, FileRecord> FreeFileRecords { get; }

        public List<FileRecord> BadRecords { get; }
        public List<FileRecord> UninitializedRecords { get; }

        public void BuildFileSystem()
        {
            //read record
            //navigate up from each filename record to parent record, keeping keys in a stack (push pop)
            //once at root, pop each from stack and build into RootDirectory
            //starting at RootDirectory, if nodes do not exist, create and add going down each level as needed
            //if it does exist, use that and keep checking down the rest of the entries
            //this will build out all the directories

            //For all directories, build out a map where key == parent directrory id and value is how to get there
            BuildDirectoryPathMap(FileRecords.Where(t=>((t.Value.EntryFlags & FileRecord.EntryFlag.IsDirectory) == FileRecord.EntryFlag.IsDirectory)));
           BuildDirectoryPathMap(FreeFileRecords.Where(t=>((t.Value.EntryFlags & FileRecord.EntryFlag.IsDirectory) == FileRecord.EntryFlag.IsDirectory)));



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

                //look for attribute list, pull out non-self referencing attributes
                var attrList =
                    (AttributeList) fileRecord.Value.Attributes.SingleOrDefault(t =>
                        t.AttributeType == AttributeType.AttributeList);

                if (attrList != null)
                {
                    foreach (var attrListAttributeInformation in attrList.AttributeInformations)
                    {
                        if (attrListAttributeInformation.EntryInfo.MftEntryNumber != fileRecord.Value.EntryNumber && attrListAttributeInformation.EntryInfo.MftSequenceNumber != fileRecord.Value.SequenceNumber)
                        {
                            _logger.Trace($"found attrlist item: {attrListAttributeInformation}");

                            var attrEntryKey = $"{attrListAttributeInformation.EntryInfo.MftEntryNumber:X8}-{attrListAttributeInformation.EntryInfo.MftSequenceNumber:X8}";

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
                    fna = GetFileNameAttributeFromFileRecord(fileRecord.Value);
                }
                var path = GetParentPathFromInUse(fna);

                var isDeleted = (fileRecord.Value.EntryFlags & FileRecord.EntryFlag.FileRecordSegmentInUse) !=
                            FileRecord.EntryFlag.FileRecordSegmentInUse;

                _directoryPathMap.Add(fileRecord.Value.Key(),path);

               var isDir = (fileRecord.Value.EntryFlags & FileRecord.EntryFlag.IsDirectory) ==
                           FileRecord.EntryFlag.IsDirectory;

                _logger.Info($"{fna.FileInfo.FileName} (is dir: {isDir} deleted: {isDeleted})> {fileRecord.Value.Key()} ==> {path}");

            }
        }

        private string GetParentPathFromInUse(FileName fileName)
        {
            var path = RootDirectory.ParentPath;

            var parentKey =   $"{fileName.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fileName.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";

            while (parentKey != RootDirectory.Key)
            {
                //traverse up the chain

                path = $"{path}\\{parentKey}";

                FileRecord parentRecord = null;

                if ((FileRecords.ContainsKey(parentKey)) || FreeFileRecords.ContainsKey(parentKey))
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
                        path = path.Replace(".", _directoryPathMap[parentKey]);

                        return path;
                    }

                    path = path.Replace(".", ".\\PathUnknown");

                    return path;
                }

                var parentFn = GetFileNameAttributeFromFileRecord(parentRecord);

                parentKey =   $"{parentFn.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{parentFn.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";
            }
            return path;
        }


        private HashSet<string> ProcesssedFileRecords = new HashSet<string>();

        private readonly Dictionary<string, string> _directoryPathMap;

      

        private FileName GetFileNameAttributeFromFileRecord(FileRecord fr)
        {
            var fi = fr.Attributes.SingleOrDefault(t =>
                t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.DosWindows);

            if (fi != null)
            {
                return (FileName) fi;
            }
            
                fi = fr.Attributes.SingleOrDefault(t =>
                    t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Windows);
            
            if (fi != null)
            {
                return (FileName) fi;
            }

            
                fi = fr.Attributes.SingleOrDefault(t =>
                    t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Posix);
            
            if (fi != null)
            {
                return (FileName) fi;
            }

           
                fi = fr.Attributes.Single(t =>
                    t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Dos);
           
                return (FileName) fi;
            
        }

//   
    }
}