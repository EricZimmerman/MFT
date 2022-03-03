using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Boot;
using FluentAssertions;
using I30;
using LogFile;
using MFT.Attributes;
using MFT.Other;
using NUnit.Framework;
using SDS;
using Secure;
using Serilog;
using Usn;

namespace MFT.Test;

[TestFixture]
public class TestMain
{
    [OneTimeSetUp]
    public void SetupNLog()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();
    }

    public static string Xwf = @"D:\Code\MFT\MFT.Test\TestFiles\xw\$MFT";
    public static string Mft4 = @"D:\Code\MFT\MFT.Test\TestFiles\NIST\DFR-16\$MFT";
    public static string Tdungan = @"D:\Code\MFT\MFT.Test\TestFiles\tdungan\$MFT";
    public static string Nromanoff = @"D:\Egnyte\Private\ezimmerman\MFTs\nromanoff\$MFT";
    public static string Nfury = @"D:\Egnyte\Private\ezimmerman\MFTs\nfury\$MFT";
    public static string Capuano = @"D:\Egnyte\Private\ezimmerman\MFTs\$MFT_FROM_CAPUANO";
    public static string Vanko = @"D:\Egnyte\Private\ezimmerman\MFTs\vanko\$MFT";
    public static string Test = @"D:\Egnyte\Private\ezimmerman\MFTs\20180615_MFTECmd_Bad_MFT_AMJH";
    public static string Test4K = @"D:\Egnyte\Private\ezimmerman\MFTs\mft_4k_mftf.dat";
    public static string OneOff = @"D:\Egnyte\Private\ezimmerman\MFTs\MFT_SymLink";

    public static string OneOff2 = @"D:\Egnyte\Private\ezimmerman\MFTs\Win10_$MFT";

    //public static string oneOff4 = @"C:\Users\eric\Desktop\$MFT\$MFT";

    [Test]
    public void OneOff3()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();


        //   LogManager.Configuration = config;

        var f = MftFile.Load(@"C:\Users\eric\OneDrive\ntfs\$MFT",false);
        Debug.WriteLine(f);

        /*var ff = f.GetDirectoryContents("00000005-00000005");

        foreach (var parentMapEntry in ff)
        {
            Debug.WriteLine(parentMapEntry);
        }

       Debug.WriteLine(f.GetFullParentPath("00000026-00000001"));

       foreach (var m2FileRecord in f.FreeFileRecords)
       {
           foreach (var attribute in m2FileRecord.Value.Attributes.Where(t =>
               t.AttributeType == AttributeType.FileName))
           {
               var fn = (FileName) attribute;
               if (fn.FileInfo.NameType == NameTypes.Dos)
               {
               }

               Debug.WriteLine(
                   $"{m2FileRecord.Value.EntryNumber},{m2FileRecord.Value.SequenceNumber},\"{f.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey())}\\{fn.FileInfo.FileName}\",Free,{m2FileRecord.Value.IsDirectory()}");
           }
       }*/

        //  var fff = MftFile.Load(@"C:\temp\$MFT");

        //89646 is where the issue is. Per this guy, 95997

        //  var aa = fff.FileRecords["89646-1"];

        //
        var f1 = new FileRecord(File.ReadAllBytes(@"C:\temp\FILE with INDX slack"), 0,true);
     //   var f2 = new FileRecord(File.ReadAllBytes(@"C:\temp\MFTECmd_FILE_Offset0x176FD.bin"), 0);
        // var f3 = new FileRecord(File.ReadAllBytes(@"C:\temp\MFTECmd_FILE_Offset0xB8A1400.bin"), 0);
        //
        Console.WriteLine(f1);
       // Console.WriteLine(f2);
        // Console.WriteLine(f3);
    }


    [Test]
    public void I30Start()
    {
        var bb = I30File.Load(@"D:\Code\MFT\MFT.Test\TestFiles\$I30\Start\$I30");

        Log.Information("Active entry count: {Count}", bb.Entries.Count(t => t.FromSlack == false));

        Log.Information("ACTIVE");
        foreach (var indexEntry in bb.Entries.Where(t => t.FromSlack == false))
        {
            Log.Information("{Ie}", indexEntry);
        }


        Log.Information("Slack entry count: {Count}", bb.Entries.Count(t => t.FromSlack));

        Log.Information("FROM SLACK");
        foreach (var indexEntry in bb.Entries.Where(t => t.FromSlack))
        {
            Log.Information("{Ie}", indexEntry);
        }
    }

    [Test]
    public void I30First()
    {
        var bb = I30File.Load(@"D:\Code\MFT\MFT.Test\TestFiles\$I30\FirstDelete\$I30");

        Log.Information("Active entry count: {Count}", bb.Entries.Count(t => t.FromSlack == false));
        Log.Information("Slack entry count: {Count}", bb.Entries.Count(t => t.FromSlack));

        foreach (var indexEntry in bb.Entries.Where(t => t.FromSlack))
        {
            Log.Information("{Ie}", indexEntry);
        }
    }

    [Test]
    public void I30Second()
    {
        var bb = I30File.Load(@"D:\Code\MFT\MFT.Test\TestFiles\$I30\SecondDelete\$I30");

        Log.Information("Active entry count: {Count}", bb.Entries.Count(t => t.FromSlack == false));
        Log.Information("Slack entry count: {Count}", bb.Entries.Count(t => t.FromSlack));
    }


    [Test]
    public void Boot()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();

        var bb = BootFile.Load(@"/home/eric/Code/MFT/MFT.Test/TestFiles/Boot/$Boot");

        Log.Information("{@Boot}", bb);

        Log.Information("$Boot.BootEntryPoint: {BootEntryPoint}", bb.BootEntryPoint);
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
    public void Rando()
    {
        var lf = SdsFile.Load(@"C:\Users\eric\OneDrive\ntfs\sds3\$SDS");

        foreach (var lfSdsEntry in lf.SdsEntries)
        {
            Console.WriteLine(lfSdsEntry);
        }

        //ss..Count.Should().Be(41);
    }

    [Test]
    public void LogFile()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();


        var lf = Log_File.Load(@"D:\SynologyDrive\ntfs\RomanoffFileSystem\$LogFile");
        //ss..Count.Should().Be(41);

//            Debug.WriteLine($"{lf.PrimaryRstrPage}");
//            Debug.WriteLine($"{lf.SecondaryRstrPage}");
//            Debug.WriteLine($"{lf.NormalPageArea.Count:N0}");
    }

    [Test]
    public void PathTest()
    {
        var m = MftFile.Load(Xwf,false);

        // --lf .\Windows\system32\config
        //find entry for config
        //if it no exist, done
        //if exist, look thru all records that have config, check parent refs against next seg, until at
        //this gets you parent ent-seq
        //need a map for parent ent-seq and children

        var f = m.GetDirectoryContents("00000005-00000005");

        foreach (var parentMapEntry in f)
        {
            Debug.WriteLine(parentMapEntry);
        }

        Debug.WriteLine("--------------------------------------");

        f = m.GetDirectoryContents("0000011F-00000003");

        foreach (var parentMapEntry in f)
        {
            Debug.WriteLine(parentMapEntry);
        }
    }

    [Test]
    public void MFTs()
    {
        var m = MftFile.Load(@"D:\Egnyte\Private\ezimmerman\MFTs\Win10_$MFT",false);

//           var f = m.GetDirectoryContents("00000005-00000005");
//
//           foreach (var parentMapEntry in f)
//           {
//               Debug.WriteLine(parentMapEntry);
//           }
//
//           Debug.WriteLine("--------------------------------------");
//
//           f = m.GetDirectoryContents("000001E4-00000019");
//
//           foreach (var parentMapEntry in f)
//           {
//               Debug.WriteLine(parentMapEntry);
//           }

        Debug.WriteLine(1);

        //ss..Count.Should().Be(41);
    }

    [Test]
    public void DollarO()
    {
        //@"D:\SynologyDrive\ntfs\$O\$O"

        using var fs = new FileStream(@"C:\Temp\ooo", FileMode.Open);
        var ea = new O.O(fs);

        foreach (var eaEntry in ea.Entries)
        {
            Debug.WriteLine(eaEntry);
        }

        ;

        //ss..Count.Should().Be(41);
    }


    [Test]
    public void LXXATTR_Solo()
    {
        var lf = File.ReadAllBytes(@"D:\Temp\Maxim_EA)STUFF_MFT_wsl2\ea-2.bin");

        var ea = new Lxattrr(lf, "LXXATTR");

        Debug.WriteLine(ea);

        //ss..Count.Should().Be(41);
    }

    [Test]
    public void LXXATTR_LXATTRB()
    {
        var lf = File.ReadAllBytes(@"D:\SynologyDrive\temp\Maxim_EA)STUFF_MFT_wsl2\MFTECmd_FILE_Offset0xABD9C00.bin");


        var ea = new FileRecord(lf, 0xABD9C00,false);


        foreach (var eaAttribute in ea.Attributes)
        {
            Debug.WriteLine(eaAttribute);
        }

        //ss..Count.Should().Be(41);
    }

    [Test]
    public void LXATTRB()
    {
        var lf = File.ReadAllBytes(@"D:\SynologyDrive\temp\Maxim_EA)STUFF_MFT_wsl2\MFTECmd_FILE_Offset0xD99C800.bin");
        //ss..Count.Should().Be(41);

        var ea = new FileRecord(lf, 0xD99C800,false);
        foreach (var eaAttribute in ea.Attributes)
        {
            Debug.WriteLine(eaAttribute);
        }
    }


    [Test]
    public void Sds()
    {
        var sds = SdsFile.Load(@"C:\Users\eric\Desktop\Failed-SDS\2");
        //ss..Count.Should().Be(41);
    }

    [Test]
    public void Sds_sds1_Secure_SDS()
    {
        var ss = SdsFile.Load(@"D:\SynologyDrive\ntfs\sds1\$Secure_$SDS");
        ss.SdsEntries.Count.Should().Be(9978);

        foreach (var ssSdsEntry in ss.SdsEntries)
        {
            //   Debug.WriteLine($"Offset: 0x{ssSdsEntry.FileOffset:X} {ssSdsEntry.SecurityDescriptor}");
        }
    }

    [Test]
    public void sds3()
    {
        var ss = SdsFile.Load(@"D:\SynologyDrive\ntfs\sds3\$SDS");
        //ss.SdsEntries.Count.Should().Be(1391);
//
//            foreach (var ssSdsEntry in ss.SdsEntries)
//            {
//                Debug.WriteLine(ssSdsEntry.SecurityDescriptor);
//            }
    }

    [Test]
    public void sds4()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();


        //var ss = SdsFile.Load(@"D:\SynologyDrive\ntfs\sds4\Win7_$SDS");
        var ss = SdsFile.Load(@"C:\Temp\sds.vss313.bin");
        ss.SdsEntries.Count.Should().Be(6868);

        foreach (var ssSdsEntry in ss.SdsEntries)
        {
            Debug.WriteLine(ssSdsEntry.SecurityDescriptor);

            if (ssSdsEntry.SecurityDescriptor.Sacl != null)
            {
                var SaclAceCount = ssSdsEntry.SecurityDescriptor.Sacl.AceCount;
                var uniqueAce = new HashSet<string>();
                foreach (var saclAceRecord in ssSdsEntry.SecurityDescriptor.Sacl.AceRecords)
                {
                    uniqueAce.Add(saclAceRecord.AceType.ToString());
                }

                var UniqueSaclAceTypes = string.Join("|", uniqueAce);
            }

            if (ssSdsEntry.SecurityDescriptor.Dacl != null)
            {
                var DaclAceCount = ssSdsEntry.SecurityDescriptor.Dacl.AceCount;
                var uniqueAce = new HashSet<string>();
                foreach (var daclAceRecord in ssSdsEntry.SecurityDescriptor.Dacl.AceRecords)
                {
                    uniqueAce.Add(daclAceRecord.AceType.ToString());
                }

                var UniqueDaclAceTypes = string.Join("|", uniqueAce);
            }
        }
    }

    [Test]
    public void Sds1_ntfs_sds2_SDS()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();

        //   LogManager.Configuration = config;


        var ss = SdsFile.Load(@"D:\SynologyDrive\ntfs\sds2\$SDS");
        ss.SdsEntries.Count.Should().Be(1696);
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

        var m2 = MftFile.Load(Nromanoff,false);

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();


        Log.Information(
            "\r\n\r\nRecord count: {FileRecordsCount:N0} free records: {FreeFileRecordsCount:N0} Bad records: {BadRecordsCount:N0} Uninit records: {UninitializedRecordsCount:N0}",
            m2.FileRecords.Count, m2.FreeFileRecords.Count, m2.BadRecords.Count, m2.UninitializedRecords.Count);

        foreach (var m2FileRecord in m2.FileRecords)
        foreach (var attribute in m2FileRecord.Value.Attributes.Where(t =>
                     t.AttributeType == AttributeType.FileName))
        {
            var fn = (FileName)attribute;
            if (fn.FileInfo.NameType == NameTypes.Dos)
            {
            }

            Debug.WriteLine(
                $"{m2FileRecord.Value.EntryNumber},{m2FileRecord.Value.SequenceNumber},\"{m2.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey())}\\{fn.FileInfo.FileName}\",InUse,{m2FileRecord.Value.IsDirectory()}");
        }

        foreach (var m2FileRecord in m2.FreeFileRecords)
        foreach (var attribute in m2FileRecord.Value.Attributes.Where(t =>
                     t.AttributeType == AttributeType.FileName))
        {
            var fn = (FileName)attribute;
            if (fn.FileInfo.NameType == NameTypes.Dos)
            {
            }

            Debug.WriteLine(
                $"{m2FileRecord.Value.EntryNumber},{m2FileRecord.Value.SequenceNumber},\"{m2.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey())}\\{fn.FileInfo.FileName}\",Free,{m2FileRecord.Value.IsDirectory()}");
        }

        var end = DateTimeOffset.Now;

        var dif = end.Subtract(start).TotalSeconds;

        Debug.WriteLine(dif);
    }


    [Test]
    public void Usn()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();


        //   LogManager.Configuration = config;

        var usn4 = UsnFile.Load(@"D:\SynologyDrive\ntfs\Troy\J-stream-testing\noname.bin");
        // var usn4 = UsnFile.Load(@"D:\!downloads\J-stream-testing\$J");
        usn4.UsnEntries.Count.Should().Be(328539);
    }

    [Test]
    public void Usn2()
    {
        var usn4 = UsnFile.Load(@"D:\SynologyDrive\ntfs\Troy\J.bin");

        //  var bb = File.ReadAllBytes(@"D:\SynologyDrive\ntfs\Troy\$J");

        var foo = UsnFile.FindStartingOffset(new FileStream(@"D:\SynologyDrive\ntfs\Troy\$J", FileMode.Open,
            FileAccess.Read));
        Debug.WriteLine(foo);

        Debug.WriteLine(usn4.UsnEntries.Last());

        usn4.UsnEntries.Count.Should().Be(142);
    }
}