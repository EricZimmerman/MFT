using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFT.Other
{
    public static class ExtensionMethods
    {
        public static string Key(this FileRecord record)
        {
            var entryNum = record.EntryNumber;
            var seqNum = record.SequenceNumber;

            if ((record.EntryFlags & FileRecord.EntryFlag.FileRecordSegmentInUse) !=
                FileRecord.EntryFlag.FileRecordSegmentInUse)
            {
                //this is free record, so decrement seqNum by one so it matches up with what is expected in ParentMFT references
                seqNum -= 1;
            }

            return $"{entryNum:X8}-{seqNum:X8}";
        }
    }   
}
