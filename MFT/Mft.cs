using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MFT.Attributes;
using NLog;
using Directory = MFT.Other.Directory;

namespace MFT
{
    public class Mft
    {
        public Mft(byte[] rawbytes)
        {
            var logger = LogManager.GetCurrentClassLogger();

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

                logger.Debug($"offset: 0x{f.Offset:X} flags: {f.EntryFlags} key: {key}");

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
            RootDirectory = new Directory("", rootKey,".");
        }

        private bool dump = false;

        public void BuildFileSystem()
        {
            //read record
            //navigate up from each filename record to parent record, keeping keys in a stack (push pop)
            //once at root, pop each from stack and build into RootDirectory
            //starting at RootDirectory, if nodes do not exist, create and add going down each level as needed
            //if it does exist, use that and keep checking down the rest of the entries
            //this will build out all the directories

            var logger = LogManager.GetCurrentClassLogger();

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

              

              
                
                foreach (var fileNameAttribute in fileRecord.Value.Attributes.Where(t=>t.AttributeType == AttributeType.FileName))
                {

                    var fna = (FileName) fileNameAttribute;

                    if (fna.FileInfo.NameType == NameTypes.Dos)
                    {
                        continue;
                    }

                    var stack = GetDirectoryChain(fna);

              

                    //the stack will always end with the RootDirectory's key, so take it away
                 stack.Pop();

                    if (fna.FileInfo.FileName.Contains("pagefile.sys"))
                    {
                        dump = false;
                        logger.Info($"***************pagefile.sys***************************");
                        
                    }
                    else
                    {
                        dump = false;
                    }

                    var startDirectory = RootDirectory;

                    if (dump)
                    {
                        logger.Info(startDirectory.Name);
                    }
                    

                    while (stack.Count>0)
                    {
                        var dirKey = stack.Pop();

                       logger.Info($"Dirkey: {dirKey}");

                        if (startDirectory.SubItems.ContainsKey(dirKey))
                        {
                            startDirectory = startDirectory.SubItems[dirKey];

                            if (dump)
                            {
                                logger.Info($"1. {startDirectory.Name} {startDirectory.ParentPath}");
                            }

                            
                        }
                        else
                        {
                            var entry = FileRecords[dirKey];

                            var newDirName = GetFileNameFromFileRecord(entry);
                            var newDirKey = $"{entry.EntryNumber:X8}-{entry.SequenceNumber:X8}";

                            var newDir = new Directory(newDirName,newDirKey,$"{startDirectory.ParentPath}");

                            if (true)
                            {
                                logger.Info($"2 {startDirectory.Name}  {startDirectory.ParentPath}");
                            }

                            startDirectory.SubItems.Add(newDirKey,newDir);

                            startDirectory = startDirectory.SubItems[newDirKey];

                            if (true)
                            {
                                logger.Info($"2.1 {startDirectory.Name}  {startDirectory.ParentPath}");
                            }

                            
                        }

                        //get fileRecord
                        //check if startDirectory.Subitems contains that key
                        //if yes, update startDir and move on
                        //if no, get dir name and add it to startDirectory, then update startDir and continue


                    }

                    string itemKey;
                    

                            var isDirectory = ((fna.FileInfo.Flags & StandardInfo.Flag.IsDirectory) == StandardInfo.Flag.IsDirectory);

                          

                    if (isDirectory)
                    {
                        itemKey = $"{fileRecord.Value.EntryNumber:X8}-{fileRecord.Value.SequenceNumber:X8}";
                    }
                    else
                    {
                        itemKey = $"{fileRecord.Value.EntryNumber:X8}-{fileRecord.Value.SequenceNumber:X8}-{fna.AttributeNumber:X8}";    
                    }

                 //   logger.Info($"itemKey: {itemKey} isDirectory: {isDirectory} fna.FileInfo.FileName: {fna.FileInfo.FileName}");

                    if (true)
                    {
                        logger.Info($"3. {startDirectory.Name}  {startDirectory.ParentPath}");
                    }

                    var itemDir = new Directory(fna.FileInfo.FileName,itemKey,$"{startDirectory.ParentPath}");

                    if (true)
                    {
                        logger.Info($"4. {itemDir.Name}  {itemDir.ParentPath}");
                        logger.Info($"5. {startDirectory.Name} {startDirectory.ParentPath} ");
                    }
                    

                    if (startDirectory.SubItems.ContainsKey(itemKey) == false)
                    {
                        startDirectory.SubItems.Add(itemKey,itemDir);
                    }

                    if (dump)
                    {
                        
                        logger.Info($"6. {startDirectory.Name} {startDirectory.ParentPath}");
                    }

                    
                }
            }
        }

        private string GetFileNameFromFileRecord(FileRecord fr)
        {
            var logger = LogManager.GetCurrentClassLogger();

            var fi = fr.Attributes.SingleOrDefault(t => t.AttributeType == AttributeType.FileName && ((FileName)t).FileInfo.NameType == NameTypes.DosWindows);
            if (fi == null)
            {
                fi = fr.Attributes.SingleOrDefault(t => t.AttributeType == AttributeType.FileName && ((FileName)t).FileInfo.NameType == NameTypes.Windows);
            }
            if (fi == null)
            {
                fi = fr.Attributes.Single(t => t.AttributeType == AttributeType.FileName && ((FileName)t).FileInfo.NameType == NameTypes.Posix);
            }

            var fin = (FileName) fi;

//            var isDirectory = ((fin.FileInfo.Flags & StandardInfo.Flag.IsDirectory) == StandardInfo.Flag.IsDirectory);
//            logger.Info($"isdir: {isDirectory}");
                            
           return fin.FileInfo.FileName;
        }

        private Stack<string> GetDirectoryChain(FileName fileName)
        {
            var stack = new Stack<string>();

            var parentKey = $"{fileName.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fileName.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";

            while (parentKey != RootDirectory.Key)
            {
                stack.Push(parentKey);

                var parentRecord = FileRecords[parentKey];

                var fileNameAttribute = (FileName) parentRecord.Attributes.First(t => t.AttributeType == AttributeType.FileName);

                parentKey = $"{fileNameAttribute.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fileNameAttribute.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";
            }

            //add the root in case things change later and we need it
            stack.Push(RootDirectory.Key);

            return stack;
        }

        public Directory RootDirectory { get; }


        public Dictionary<string, FileRecord> FileRecords { get; }
        public Dictionary<string, FileRecord> FreeFileRecords { get; }

        public List<FileRecord> BadRecords { get; }
        public List<FileRecord> UninitializedRecords { get; }
    }
}