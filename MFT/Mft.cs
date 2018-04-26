using System;
using System.Collections.Generic;
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

            //https://github.com/libyal/libfsntfs/blob/master/documentation/New%20Technologies%20File%20System%20(NTFS).asciidoc#file_attribute_flags

            //this is a list of file entries that are extensions of other records. record them in here, then when done processing, link together with others.
            //    var relatedRecordKeys = new Dictionary<string,string>();

            while (index < rawbytes.Length)
            {
                Buffer.BlockCopy(rawbytes, index, fileBytes, 0, blockSize);

                var f = new FileRecord(fileBytes, index);

                

                var key = $"{f.EntryNumber}-{f.SequenceNumber}";

                logger.Debug($"offset: 0x{f.Offset:X} flags: {f.EntryFlags} key: {key}");

                //   var list = string.Empty;

                if ((f.EntryFlags & FileRecord.EntryFlag.FileRecordSegmentInUse) ==
                    FileRecord.EntryFlag.FileRecordSegmentInUse)
                {
                    FileRecords.Add(key, f);
                    //        list = nameof(FileRecords);
                }
                else if (f.IsBad)
                {
                    BadRecords.Add(f);
                    //         list = nameof(BadRecords);
                }
                else if (f.IsUninitialized)
                {
                    UninitializedRecords.Add(f);
                    //       list = nameof(UninitializedRecords);
                }
                else
                {
                    FreeFileRecords.Add(key, f);
                    //        list = nameof(FreeFileRecords);
                }

//                if (f.MftRecordToBaseRecord.MftEntryNumber > 0)
//                {
//                    relatedRecordKeys.Add(key, list);
//                }

                index += blockSize;
            }

        }

        public Dictionary<string, FileRecord> FileRecords { get; }
        public Dictionary<string, FileRecord> FreeFileRecords { get; }

        public List<FileRecord> BadRecords { get; }
        public List<FileRecord> UninitializedRecords { get; }
    }
}