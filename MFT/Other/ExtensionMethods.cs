using System.Collections.Generic;
using System.Linq;
using MFT.Attributes;

namespace MFT.Other
{
    public static class ExtensionMethods
    {
        public static int GetReferenceCount(this FileRecord record)
        {
            var fns = record.Attributes.Where(t => t.AttributeType == AttributeType.FileName);

            var hashes = new HashSet<string>();

            foreach (var attribute in fns)
            {
                var fn = (FileName) attribute;
                if (fn.FileInfo.NameType == NameTypes.Dos)
                {
                    continue;
                }

                var key = $"{fn.FileInfo.FileName}-{fn.FileInfo.ParentMftRecord.GetKey()}";

                hashes.Add(key);
            }

            return hashes.Count;
        }

        public static ReparsePoint GetReparsePoint(this FileRecord record)
        {
            var reparseAttr =
                record.Attributes.Where(t =>
                    t.AttributeType == AttributeType.ReparsePoint).ToList();

            return (ReparsePoint) reparseAttr.FirstOrDefault();
        }

        public static FileName GetFileNameAttributeFromFileRecord(this FileRecord fr) //, int attributeNumber = -1
        {
//            if (attributeNumber > -1)
//            {
//                var fin = fr.Attributes.SingleOrDefault(t =>
//                    t.AttributeType == AttributeType.FileName && ((FileName) t).AttributeNumber == attributeNumber);
//
//                return (FileName) fin;
//            }
            var fi = fr.Attributes.FirstOrDefault(t =>
                t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.DosWindows);

            if (fi != null)
            {
                return (FileName) fi;
            }

            fi = fr.Attributes.FirstOrDefault(t =>
                t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Windows);

            if (fi != null)
            {
                return (FileName) fi;
            }


            fi = fr.Attributes.FirstOrDefault(t =>
                t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Posix);

            if (fi != null)
            {
                return (FileName) fi;
            }


            fi = fr.Attributes.SingleOrDefault(t =>
                t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Dos);

            return (FileName) fi;
        }

        public static string GetKey(this MftEntryInfo mftInfo, bool asDecimal = false)
        {
            if (asDecimal)
            {
                return $"{mftInfo.MftEntryNumber}-{mftInfo.MftSequenceNumber}";
            }
            return $"{mftInfo.MftEntryNumber:X8}-{mftInfo.MftSequenceNumber:X8}";
            
        }

        public static string GetKey(this FileRecord record, bool asDecimal = false)
        {
            if (asDecimal)
            {
                if (record.IsDeleted())
                {
                    return $"{record.EntryNumber}-{record.SequenceNumber - 1}";
                }

                return $"{record.EntryNumber}-{record.SequenceNumber}";
            }


            if (record.IsDeleted())
            {
                return $"{record.EntryNumber:X8}-{record.SequenceNumber - 1:X8}";
            }

            return $"{record.EntryNumber:X8}-{record.SequenceNumber:X8}";
        }

        public static bool IsDirectory(this FileRecord record)
        {
            if (record == null)
            {
                return false;
            }

            return (record.EntryFlags & FileRecord.EntryFlag.IsDirectory) ==
                   FileRecord.EntryFlag.IsDirectory;
        }

        public static bool IsDeleted(this FileRecord record)
        {
            return (record.EntryFlags & FileRecord.EntryFlag.InUse) !=
                   FileRecord.EntryFlag.InUse;
        }

        public static bool HasAds(this FileRecord record)
        {
            var dataAttrs =
                record.Attributes.Where(t =>
                    t.AttributeType == AttributeType.Data && t.NameSize > 0).ToList();

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

                if (da.IsResident == false && da.NonResidentData.StartingVirtualClusterNumber > 0)
                {
                    continue;
                }

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
            if (record.IsDirectory())
            {
                return 0;
            }

            var fn = record.Attributes.FirstOrDefault(t => t.AttributeType == AttributeType.FileName);

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

            if (datas.Count != 0)
            {
                return 0;
            }


            if (fn != null)
            {
                var fna = (FileName) fn;
                return fna.FileInfo.LogicalSize;
            }

            return 0;
        }
    }
}