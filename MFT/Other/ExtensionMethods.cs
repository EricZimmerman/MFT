using System.Collections.Generic;
using System.Linq;
using MFT.Attributes;

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

        public static bool IsDirectory(this FileRecord record)
        {
            return (record.EntryFlags & FileRecord.EntryFlag.IsDirectory) ==
                   FileRecord.EntryFlag.IsDirectory;
        }
        public static bool IsDeleted(this FileRecord record)
        {
            return (record.EntryFlags & FileRecord.EntryFlag.FileRecordSegmentInUse) !=
                   FileRecord.EntryFlag.FileRecordSegmentInUse;
        }

        public static bool HasAds(this FileRecord record)
        {
            var dataAttrs =
                record.Attributes.Where(t =>
                    t.AttributeType == AttributeType.Data && t.NameSize>0).ToList();

            return dataAttrs.Count > 0;
        }

        public static List<AdsInfo> GetAlternateDataStreams(this FileRecord record)
        {
            var l = new List<AdsInfo>();

            var dataAttrs =
                record.Attributes.Where(t =>
                    t.AttributeType == AttributeType.Data && t.NameSize > 0).ToList();

            foreach (var attribute in dataAttrs)
            {
                var da = (Data) attribute;

                ulong size;
                if (da.IsResident)
                {
                    size = (ulong) da.AttributeContentLength;
                }
                else
                {
                    size = da.NonResidentData.ActualSize;
                }

                var adsi = new AdsInfo(da.Name, size, da.ResidentData, da.NonResidentData);

                l.Add(adsi);
            }

            return l;
        }

        public static ulong GetFileSize(this FileRecord record)
        {
            var fn = record.Attributes.FirstOrDefault(t => t.AttributeType == AttributeType.FileName);
            if (fn != null)
            {
                if (record.IsDirectory())
                {
                    return 0;
                }
            }

            var datas = record.Attributes.Where(t => t.AttributeType == AttributeType.Data).ToList();

            if (datas.Count >= 1)
            {
                var data = (Data) datas.First();

                if (data.IsResident)
                {
                    return (ulong) data.ResidentData.Data.LongLength;
                }

                return data.NonResidentData.ActualSize;
            }

            if (datas.Count == 0)
            {
                var fna = (FileName) fn;
                if (fn != null)
                {
                    return fna.FileInfo.LogicalSize;
                }
            }

            return 0;
        }
    }
}