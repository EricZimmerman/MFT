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

                if ((f.EntryFlags & FileRecord.EntryFlag.FileRecordSegmentInUse) ==
                    FileRecord.EntryFlag.FileRecordSegmentInUse)
                {
                    FileRecords.Add(key, f);
                }
                else if (f.IsBad)
                {
                    BadRecords.Add(f);
                }
                else if (f.IsUninitialized)
                {
                    UninitializedRecords.Add(f);
                }
                else
                {
                    FreeFileRecords.Add(key, f);
                }

                index += blockSize;
            }

            var rootFolder = FileRecords.Single(t => t.Value.EntryNumber == 5).Value;
            var rootKey = $"{rootFolder.EntryNumber:X8}-{rootFolder.SequenceNumber:X8}";
            RootDirectory = new DirectoryItem("", rootKey, ".",false,null,rootFolder.GetFileSize(),false,false);
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

            foreach (var fileRecord in FileRecords)
            {
                //   logger.Info(fileRecord.Value);

                if (fileRecord.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                    fileRecord.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
                {
                    //will get this record via attributeList
                    continue;
                }

                var key = $"{fileRecord.Value.EntryNumber:X8}-{fileRecord.Value.SequenceNumber:X8}";

                if (RootDirectory.Key == key)
                {
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

                //data block count for ads
                var dataAttrs =
                    fileRecord.Value.Attributes.Where(t =>
                        t.AttributeType == AttributeType.Data && t.NameSize>0).ToList();

                var hasAds = fileRecord.Value.GetAlternateDataStreams();// dataAttrs.Count > 0;

                if (hasAds.Count > 0)
                {
                   _logger.Trace($"Found {dataAttrs.Count:N0} ADSs");
                }

                var reparseAttr =
                    fileRecord.Value.Attributes.Where(t =>
                        t.AttributeType == AttributeType.ReparsePoint).ToList();

                var reparsePoint = (ReparsePoint) reparseAttr.FirstOrDefault();

                if (reparsePoint != null)
                {
                    _logger.Trace($"Found reparse point: {reparsePoint.PrintName} --> {reparsePoint.SubstituteName}");
                }

                var baseEntryNumber = -1;

                foreach (var fileNameAttribute in fileRecord.Value.Attributes.Where(t =>
                    t.AttributeType == AttributeType.FileName))
                {
                    var fna = (FileName) fileNameAttribute;

                    if (fna.FileInfo.NameType == NameTypes.Dos)
                    {
                        continue;
                    }

                    if (baseEntryNumber == -1)
                    {
                        baseEntryNumber = (int) fna.FileInfo.ParentMftRecord.MftEntryNumber;
                      
                    }

                    var isHardLink = false;
                    isHardLink = (fna.FileInfo.ParentMftRecord.MftEntryNumber != baseEntryNumber );

                    var stack = GetDirectoryChain(fna);

                    //the stack will always end with the RootDirectory's key, so take it away
                    stack.Pop();

                    var startDirectory = RootDirectory;

                    var parentDir = ".";

                    while (stack.Count > 0)
                    {
                        var dirKey = stack.Pop();

                        if (startDirectory.SubItems.ContainsKey(dirKey))
                        {
                            startDirectory = startDirectory.SubItems[dirKey];

                            parentDir = $"{parentDir}\\{startDirectory.Name}";
                        }
                        else
                        {
                            var entry = FileRecords[dirKey];

                            var newDirName = GetFileNameFromFileRecord(entry);
                            var newDirKey = $"{entry.EntryNumber:X8}-{entry.SequenceNumber:X8}";

                            var newDir = new DirectoryItem(newDirName, newDirKey, parentDir,false,reparsePoint,0,false,false);

                            startDirectory.SubItems.Add(newDirKey, newDir);

                            startDirectory = startDirectory.SubItems[newDirKey];
                        }
                    }

                    string itemKey;

                    var isDirectory = (fileRecord.Value.EntryFlags & FileRecord.EntryFlag.IsDirectory) ==
                                      FileRecord.EntryFlag.IsDirectory;

                    ulong fileSize = 0;
                    if (isDirectory)
                    {
                        itemKey = $"{fileRecord.Value.EntryNumber:X8}-{fileRecord.Value.SequenceNumber:X8}";
                    }
                    else
                    {
                        itemKey =
                            $"{fileRecord.Value.EntryNumber:X8}-{fileRecord.Value.SequenceNumber:X8}-{fna.AttributeNumber:X8}";
                        fileSize = fileRecord.Value.GetFileSize();
                    }

                    var itemDir = new DirectoryItem(fna.FileInfo.FileName, itemKey, parentDir,hasAds.Count>0,reparsePoint,fileSize,isHardLink,false);

                    if (startDirectory.SubItems.ContainsKey(itemKey) == false)
                    {
                        startDirectory.SubItems.Add(itemKey, itemDir);
                    }
                }
            }

            ProcessFreeRecords();
        }

        private void ProcessFreeRecords()
        {

            var freeDirectories = FreeFileRecords.Where(t =>
                (t.Value.EntryFlags & FileRecord.EntryFlag.IsDirectory) == FileRecord.EntryFlag.IsDirectory).ToList();

            //put free directories where they belong

            var notFoundRecords = new List<FileRecord>(); //contains FileRecords that could not be attached to an existing directory. These will end up under ".\Path unknown"

            foreach (var freeDirectory in freeDirectories)
            {
                var key = $"{freeDirectory.Value.EntryNumber:X8}-{freeDirectory.Value.SequenceNumber:X8}";

                //look for attribute list, pull out non-self referencing attributes
                var attrList =
                    (AttributeList) freeDirectory.Value.Attributes.SingleOrDefault(t =>
                        t.AttributeType == AttributeType.AttributeList);

                if (attrList != null)
                {
                    foreach (var attrListAttributeInformation in attrList.AttributeInformations)
                    {
                        if (attrListAttributeInformation.EntryInfo.MftEntryNumber != freeDirectory.Value.EntryNumber && attrListAttributeInformation.EntryInfo.MftSequenceNumber != freeDirectory.Value.SequenceNumber)
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
                                freeDirectory.Value.Attributes.AddRange(attrEntry.Attributes);    
                            }
                        }
                    }
                }

                var reparseAttr =
                    freeDirectory.Value.Attributes.Where(t =>
                        t.AttributeType == AttributeType.ReparsePoint).ToList();

                var reparsePoint = (ReparsePoint) reparseAttr.FirstOrDefault();

                if (reparsePoint != null)
                {
                    _logger.Trace($"Found reparse point: {reparsePoint.PrintName} --> {reparsePoint.SubstituteName}");
                }

                foreach (var fileNameAttribute in freeDirectory.Value.Attributes.Where(t =>
                    t.AttributeType == AttributeType.FileName))
                {
                    var fna = (FileName) fileNameAttribute;

                    if (fna.FileInfo.NameType == NameTypes.Dos)
                    {
                        continue;
                    }

                    var stack = GetDirectoryChain(fna);

                    if (stack.Count == 0)
                    {
                        if (RootDirectory.SubItems.ContainsKey("PathUnknown") == false)
                        {
                            var punk = new DirectoryItem("Path unknown","PathUnknown",".",false,null,0,false,true);
                            RootDirectory.SubItems.Add("PathUnknown",punk);
                        }

                        var pu = RootDirectory.SubItems["PathUnknown"];

                        var someDirEntryKey1 = $"{fna.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fna.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";

                        var someDir = new DirectoryItem($"Directory with entry 0x{fna.FileInfo.ParentMftRecord.MftEntryNumber:x}",someDirEntryKey1,".\\Path unknown",false,reparsePoint,0,false,true);
                        if (pu.SubItems.ContainsKey(someDirEntryKey1) == false)
                        {
                            pu.SubItems.Add(someDirEntryKey1,someDir);
                        }

                        stack.Push(someDirEntryKey1);
                        stack.Push("PathUnknown");
                        stack.Push("FakeRoot");
                    }

                    //the stack will always end with the RootDirectory's key, so take it away
                    stack.Pop();

                    var startDirectory = RootDirectory;

                    var parentDir = ".";

                    while (stack.Count > 0)
                    {
                        var dirKey = stack.Pop();

                        if (startDirectory.SubItems.ContainsKey(dirKey))
                        {
                            startDirectory = startDirectory.SubItems[dirKey];

                            parentDir = $"{parentDir}\\{startDirectory.Name}";
                        }
                        else
                        {
                            var entry = FileRecords[dirKey];

                            var newDirName = GetFileNameFromFileRecord(entry);
                            var newDirKey = $"{entry.EntryNumber:X8}-{entry.SequenceNumber:X8}";

                            var newDir = new DirectoryItem(newDirName, newDirKey, parentDir,false,reparsePoint,0,false,false);

                            startDirectory.SubItems.Add(newDirKey, newDir);

                            startDirectory = startDirectory.SubItems[newDirKey];
                        }
                    }
                 
                    var    itemKey = $"{freeDirectory.Value.EntryNumber:X8}-{freeDirectory.Value.SequenceNumber:X8}";

                    var itemDir = new DirectoryItem(fna.FileInfo.FileName, itemKey, parentDir,false,reparsePoint,0,false,true);

                    if (startDirectory.SubItems.ContainsKey(itemKey) == false)
                    {
                        startDirectory.SubItems.Add(itemKey, itemDir);
                    }
                }
            }


        }

        private string GetFileNameFromFileRecord(FileRecord fr)
        {
            var fi = fr.Attributes.SingleOrDefault(t =>
                t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.DosWindows);
            if (fi == null)
            {
                fi = fr.Attributes.SingleOrDefault(t =>
                    t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Windows);
            }

            if (fi == null)
            {
                fi = fr.Attributes.SingleOrDefault(t =>
                    t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Posix);
            }

            if (fi == null)
            {
                fi = fr.Attributes.Single(t =>
                    t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Dos);
            }

            var fin = (FileName) fi;

            return fin.FileInfo.FileName;
        }

        private Stack<string> GetDirectoryChain(FileName fileName)
        {
            var stack = new Stack<string>();

            var parentKey =
                $"{fileName.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fileName.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";

            while (parentKey != RootDirectory.Key)
            {
                if (FileRecords.ContainsKey(parentKey) == false)
                {
                    return stack;
                }

                stack.Push(parentKey);

                var parentRecord = FileRecords[parentKey];

                var fileNameAttribute =
                    (FileName) parentRecord.Attributes.First(t => t.AttributeType == AttributeType.FileName);

                parentKey =
                    $"{fileNameAttribute.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fileNameAttribute.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";
            }

            //add the root in case things change later and we need it
            stack.Push(RootDirectory.Key);

            return stack;
        }
    }
}