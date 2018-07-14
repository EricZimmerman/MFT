using System;
using System.Diagnostics;
using System.Linq;
using MFT.Attributes;
using MFT.Other;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;

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
        public void Something()
        {
            var start = DateTimeOffset.Now;

            var m2 = MftFile.Load(Vanko);

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
    }
}