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
            RootDirectory = new Directory(".", rootKey);
        }

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

                var sia = fileRecord.Value.Attributes.Single(t =>
                    t.AttributeType == AttributeType.StandardInformation);
                var si = (StandardInfo) sia;

                var isDirectory = ((si.Flags & StandardInfo.Flag.IsDirectory) == StandardInfo.Flag.IsDirectory);
                
                foreach (var fileNameAttribute in fileRecord.Value.Attributes.Where(t=>t.AttributeType == AttributeType.FileName))
                {
                    var fna = (FileName) fileNameAttribute;

                    var stack = GetDirectoryChain(fna);

                    logger.Info($"fna: {fna.FileInfo.FileName} ==> {string.Join("|",stack.ToList())}");

                    //the stack will always end with the RootDirectory's key, so take it away

                    stack.Pop();

                    var startDirectory = RootDirectory;

                    while (stack.Count>0)
                    {
                        var dirKey = stack.Pop();

                        //get fileRecord
                        //check if startDirectory.Subitems contains that key
                        //if yes, update startDir and move on
                        //if no, get dir name and add it to startDirectory, then update startDir and continue
                        

                    }
                }
            }

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