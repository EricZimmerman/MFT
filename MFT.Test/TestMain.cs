using System;
using System.Diagnostics;
using System.Linq;
using Boot;
using FluentAssertions;
using MFT.Attributes;
using MFT.Other;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using SDS;
using Secure;
using Usn;

namespace MFT.Test
{
    [TestFixture]
    public class TestMain
    {
        public static string xwf = @"D:\Code\MFT\MFT.Test\TestFiles\xw\$MFT";
        public static string Mft4 = @"D:\Code\MFT\MFT.Test\TestFiles\NIST\DFR-16\$MFT";
        public static string tdungan = @"D:\Code\MFT\MFT.Test\TestFiles\tdungan\$MFT";
        public static string nromanoff = @"D:\SynologyDrive\MFTs\nromanoff\$MFT";
        public static string nfury = @"D:\SynologyDrive\MFTs\nfury\$MFT";
        public static string CAPUANO = @"D:\SynologyDrive\MFTs\$MFT_FROM_CAPUANO";
        public static string Vanko = @"D:\Egnyte\Private\ezimmerman\MFTs\vanko\$MFT";
        public static string test = @"D:\SynologyDrive\MFTs\20180615_MFTECmd_Bad_MFT_AMJH";
        public static string test4k = @"D:\Egnyte\Private\ezimmerman\MFTs\mft_4k_mftf.dat";
        public static string oneOff = @"D:\Egnyte\Private\ezimmerman\MFTs\MFT_SymLink";

        public static string oneOff2 = @"D:\Egnyte\Private\ezimmerman\MFTs\Win10_$MFT";
        //public static string oneOff4 = @"C:\Users\eric\Desktop\$MFT\$MFT";


        [OneTimeSetUp]
        public void SetupNLog()
        {
            LogManager.Configuration = null;

            var config = new LoggingConfiguration();
            var loglevel = LogLevel.Info;

            var layout = @"${message}";

            var consoleTarget = new ColoredConsoleTarget();

            config.AddTarget("console", consoleTarget);

            consoleTarget.Layout = layout;

            var rule1 = new LoggingRule("*", loglevel, consoleTarget);
            config.LoggingRules.Add(rule1);

            LogManager.Configuration = config;
        }


        [Test]
        public void Boot()
        {
            var bb = BootFile.Load(@"..\..\TestFiles\Boot\$Boot");

            Debug.WriteLine($"$Boot.BootEntryPoint: {bb.BootEntryPoint}");
            Debug.WriteLine($"$Boot.FileSystemSignature: {bb.FileSystemSignature}");

            Debug.WriteLine($"$Boot.BytesPerSector: {bb.BytesPerSector}");
            Debug.WriteLine($"$Boot.SectorsPerCluster: {bb.SectorsPerCluster}");
            Debug.WriteLine($"$Boot.ReservedSectors: {bb.ReservedSectors}");
            Debug.WriteLine($"$Boot.MediaDescriptor: {bb.MediaDescriptor:X} ({bb.DecodeMediaDescriptor()})");

            Debug.WriteLine($"$Boot.SectorsPerTrack: {bb.SectorsPerTrack}");
            Debug.WriteLine($"$Boot.NumberOfHeads: {bb.NumberOfHeads}");
            Debug.WriteLine($"$Boot.NumberOfHiddenSectors: {bb.NumberOfHiddenSectors}");

            Debug.WriteLine($"$Boot.TotalSectors: {bb.TotalSectors}");

            Debug.WriteLine($"$Boot.MftClusterBlockNumber: {bb.MftClusterBlockNumber}");
            Debug.WriteLine($"$Boot.MirrorMftClusterBlockNumber: {bb.MirrorMftClusterBlockNumber}");

            Debug.WriteLine($"$Boot.MftEntrySize: {bb.MftEntrySize}");
            Debug.WriteLine($"$Boot.IndexEntrySize: {bb.IndexEntrySize}");

            Debug.WriteLine($"$Boot.VolumeSerialNumber 64: {bb.GetVolumeSerialNumber()}");
            Debug.WriteLine($"$Boot.VolumeSerialNumber 32: {bb.GetVolumeSerialNumber(true)}");
            Debug.WriteLine($"$Boot.VolumeSerialNumber 32 rev: {bb.GetVolumeSerialNumber(true, true)}");


            Debug.WriteLine($"$Boot.SectorSignature: {bb.GetSectorSignature()}");
        }

        [Test]
        public void Sdh()
        {
            var sdh = SdhFile.Load(@"D:\Temp\ntfs\sds2\$Sdh");
            //ss..Count.Should().Be(41);
        }

        [Test]
        public void Sds_sds1_Secure_SDS()
        {
            var ss = SdsFile.Load(@"D:\Temp\ntfs\sds1\$Secure_$SDS");
            ss.SdsEntries.Count.Should().Be(9978);

            foreach (var ssSdsEntry in ss.SdsEntries)
            {
                Debug.WriteLine($"Offset: 0x{ssSdsEntry.FileOffset:X} {ssSdsEntry.SecurityDescriptor}");
            }
        }

        [Test]
        public void Sds1_ntfs_sds2_SDS()
        {
            var ss = SdsFile.Load(@"D:\Temp\ntfs\sds2\$SDS");
            ss.SdsEntries.Count.Should().Be(1391);
//
//            foreach (var ssSdsEntry in ss.SdsEntries)
//            {
//                Debug.WriteLine(ssSdsEntry.SecurityDescriptor);
//            }
        }

        [Test]
        public void Sii()
        {
            var ssi = SiiFile.Load(@"D:\Temp\ntfs\sds2\$Sii");
            //ss..Count.Should().Be(41);
        }

        [Test]
        public void Something()
        {
//            var foo = new MFT.FileRecord(File.ReadAllBytes(@"D:\Temp\ProblemFRS1"),1);
//            var foo1 = new MFT.FileRecord(File.ReadAllBytes(@"D:\Temp\ProblemFRS2"),1);
//            

            var start = DateTimeOffset.Now;

            var m2 = MftFile.Load(xwf);

            var logger = LogManager.GetCurrentClassLogger();

            logger.Info(
                $"\r\n\r\nRecord count: {m2.FileRecords.Count:N0} free records: {m2.FreeFileRecords.Count:N0} Bad records: {m2.BadRecords.Count:N0} Uninit records: {m2.UninitializedRecords.Count:N0}");

            foreach (var m2FileRecord in m2.FileRecords)
            {
                foreach (var attribute in m2FileRecord.Value.Attributes.Where(t =>
                    t.AttributeType == AttributeType.FileName))
                {
                    var fn = (FileName) attribute;
                    if (fn.FileInfo.NameType == NameTypes.Dos)
                    {
                    }

                    Debug.WriteLine(
                        $"{m2FileRecord.Value.EntryNumber},{m2FileRecord.Value.SequenceNumber},\"{m2.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey())}\\{fn.FileInfo.FileName}\",InUse,{m2FileRecord.Value.IsDirectory()}");
                }
            }

            foreach (var m2FileRecord in m2.FreeFileRecords)
            {
                foreach (var attribute in m2FileRecord.Value.Attributes.Where(t =>
                    t.AttributeType == AttributeType.FileName))
                {
                    var fn = (FileName) attribute;
                    if (fn.FileInfo.NameType == NameTypes.Dos)
                    {
                    }

                    Debug.WriteLine(
                        $"{m2FileRecord.Value.EntryNumber},{m2FileRecord.Value.SequenceNumber},\"{m2.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey())}\\{fn.FileInfo.FileName}\",Free,{m2FileRecord.Value.IsDirectory()}");
                }
            }

            var end = DateTimeOffset.Now;

            var dif = end.Subtract(start).TotalSeconds;

            Debug.WriteLine(dif);
        }


        [Test]
        public void Usn()
        {
//            var usn1 = UsnFile.Load(@"..\..\TestFiles\Usn\record.usn");
//            usn1.UsnEntries.Count.Should().Be(1);
//            //Debug.WriteLine(usn1.UsnEntries.First().ToString());
//
//            var usn2 = UsnFile.Load(@"D:\Temp\ntfs\testUsn.bin");
//            usn2.UsnEntries.Count.Should().Be(41);
//            
//            foreach (var usn2UsnEntry in usn2.UsnEntries)
//            {
//                Debug.WriteLine(usn2UsnEntry.ToString());
//            }


//            var usn3 = UsnFile.Load(@"D:\Temp\ntfs\Troy\$J");
//usn3.UsnEntries.Count.Should().Be(328539);
//            foreach (var usn2UsnEntry in usn3.UsnEntries)
//            {
//                Debug.WriteLine(usn2UsnEntry.ToString());
//            }

            var usn4 = UsnFile.Load(@"D:\Temp\ntfs\vssJ\vss1-usnjrnl");
            usn4.UsnEntries.Count.Should().Be(38948);
        }
    }
}