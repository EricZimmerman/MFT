using System;
using System.IO;
using System.Text;
using NLog;

namespace Boot
{
    public class Boot
    {
        private readonly Logger _logger = LogManager.GetLogger("Boot");

        public Boot(Stream fileStream)
        {
            const int expectedSectorSig = 0xaa55;

            var rawBytes = new byte[512];
            fileStream.Read(rawBytes, 0, 512);

            SectorSignature = BitConverter.ToUInt16(rawBytes, 510);

            if (SectorSignature != expectedSectorSig)
            {
                _logger.Warn(
                    $"Expected signature (0x55 0xAA) not found at offset 0x1FE. Value found: {GetSectorSignature()}");
            }

            BootEntryPoint = $"0x{rawBytes[0]:X2} 0x{rawBytes[1]:X2} 0x{rawBytes[2]:X2}";

            FileSystemSignature = Encoding.ASCII.GetString(rawBytes, 3, 8);

            BytesPerSector = BitConverter.ToInt16(rawBytes, 11);
            SectorsPerCluster = rawBytes[13];

            ReservedSectors = BitConverter.ToInt16(rawBytes, 14);
            NumberOfFaTs = rawBytes[16];

            RootDirectoryEntries = BitConverter.ToInt16(rawBytes, 17);
            TotalNumberOfSectors16 = BitConverter.ToInt16(rawBytes, 19);

            MediaDescriptor = rawBytes[21];

            SectorsPerFat = BitConverter.ToInt16(rawBytes, 22);

            SectorsPerTrack = BitConverter.ToInt16(rawBytes, 24);
            NumberOfHeads = BitConverter.ToInt16(rawBytes, 26);
            NumberOfHiddenSectors = BitConverter.ToInt32(rawBytes, 28);
            TotalNumberOfSectors = BitConverter.ToInt32(rawBytes, 32);

            DiskUnitNumber = rawBytes[36];
            UnknownFlags = rawBytes[37];
            BpbVersionSignature = rawBytes[38];
            UnknownReserved = rawBytes[39];

            TotalSectors = BitConverter.ToInt64(rawBytes, 40);
            MftClusterBlockNumber = BitConverter.ToInt64(rawBytes, 48);
            MirrorMftClusterBlockNumber = BitConverter.ToInt64(rawBytes, 56);

            var clusterSize = BytesPerSector * SectorsPerCluster;

            var mftEntrySize = rawBytes[64];

            MftEntrySize = GetSizeAsBytes(mftEntrySize, clusterSize);

            var indexEntrySize = rawBytes[68];

            IndexEntrySize = GetSizeAsBytes(indexEntrySize, clusterSize);

            VolumeSerialNumberRaw = BitConverter.ToInt64(rawBytes, 72);

            Checksum = BitConverter.ToInt32(rawBytes, 80);
        }

        public string BootEntryPoint { get; }
        public string FileSystemSignature { get; }

        public int BytesPerSector { get; }
        public int SectorSignature { get; }
        public int SectorsPerCluster { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public int ReservedSectors { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public int NumberOfFaTs { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public int RootDirectoryEntries { get; }

        public int TotalNumberOfSectors16 { get; }

        public byte MediaDescriptor { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public int SectorsPerFat { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public int SectorsPerTrack { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public int NumberOfHeads { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public int NumberOfHiddenSectors { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public int TotalNumberOfSectors { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public byte DiskUnitNumber { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public byte UnknownFlags { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public byte BpbVersionSignature { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public byte UnknownReserved { get; }

        public long TotalSectors { get; }
        public long MftClusterBlockNumber { get; }
        public long MirrorMftClusterBlockNumber { get; }

        /// <summary>
        ///     As bytes
        /// </summary>
        public int MftEntrySize { get; }

        /// <summary>
        ///     As bytes
        /// </summary>
        public int IndexEntrySize { get; }

        /// <summary>
        ///     Use GetVolumeSerialNumber() to convert to different forms
        /// </summary>
        public long VolumeSerialNumberRaw { get; }

        /// <summary>
        ///     Not used by NTFS
        /// </summary>
        public int Checksum { get; }

        public string DecodeMediaDescriptor()
        {
            var desc = new StringBuilder();

            var mdBits = Convert.ToString(MediaDescriptor, 2);

            switch (mdBits[0])
            {
                case '0':
                    desc.Append("Single-sided");
                    break;
                default:
                    desc.Append("Double-sided");
                    break;
            }

            switch (mdBits[1])
            {
                case '0':
                    desc.Append(", 9 sectors per track");
                    break;
                default:
                    desc.Append(", 8 sectors per track");
                    break;
            }

            switch (mdBits[2])
            {
                case '0':
                    desc.Append(", 80 tracks");
                    break;
                default:
                    desc.Append(", 40 tracks");
                    break;
            }

            switch (mdBits[3])
            {
                case '0':
                    desc.Append(", Fixed disc");
                    break;
                default:
                    desc.Append(", Removable disc");
                    break;
            }

            return desc.ToString();
        }

        public string GetSectorSignature()
        {
            var b = BitConverter.GetBytes(SectorSignature);
            return $"{b[0]:X2} {b[1]:X2}";
        }

        public string GetVolumeSerialNumber(bool as32Bit = false, bool reverse = false)
        {
            var b = BitConverter.GetBytes(VolumeSerialNumberRaw);

            var sn = string.Empty;

            if (as32Bit)
            {
                if (reverse)
                {
                    for (var i = 3; i > -1; i--)
                    {
                        sn = $"{sn} {b[i]:X2}";
                    }
                }
                else
                {
                    for (var i = 0; i < 4; i++)
                    {
                        sn = $"{sn} {b[i]:X2}";
                    }
                }

                return sn.Trim();
            }

            for (var i = 0; i < 8; i++)
            {
                sn = $"{sn} {b[i]:X2}";
            }

            return sn.Trim();
        }

        private static int GetSizeAsBytes(byte size, int clusterSize)
        {
            if (size <= 127)
            {
                return size * clusterSize;
            }

            if (size > 127 && size <= 255)
            {
                return (int) Math.Pow(2, 256 - size);
            }

            return 0;
        }
    }
}