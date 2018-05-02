using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using Directory = MFT.Other.DirectoryItem;

namespace MFT.Test
{
    [TestFixture]
    public class TestMain
    {
        public static string Mft1 = @"..\..\TestFiles\$MFT";
        public static string xwf = @"D:\Code\MFT\MFT.Test\TestFiles\xw\$MFT";
        public static string Mft4 = @"D:\Code\MFT\MFT.Test\TestFiles\NIST\DFR-16\$MFT";
        public static string tdungan = @"D:\Code\MFT\MFT.Test\TestFiles\tdungan\$MFT";
        public static string nromanoff = @"D:\SynologyDrive\MFTs\nromanoff\$MFT";
        public static string nfury = @"D:\SynologyDrive\MFTs\nfury\$MFT";
        public static string CAPUANO = @"D:\SynologyDrive\MFTs\$MFT_FROM_CAPUANO";

        private void DumpFiles(Directory dir)
        {
            var logger = LogManager.GetCurrentClassLogger();


            logger.Info($"Path: {dir.ParentPath}\\{dir.Name} Item count: ({dir.SubItems.Count:N0})");

            foreach (var subitem in dir.SubItems.Values.OrderByDescending(t => t.SubItems.Count > 0)
                .ThenBy(t => t.Name))
            {
                var reparse = string.Empty;

                if (subitem.ReparsePoint!=null)
                {
                    if (subitem.ReparsePoint.PrintName.Length > 0)
                    {
                        reparse = $"Reparse: {subitem.ReparsePoint.PrintName} --> {subitem.ReparsePoint.SubstituteName.Replace(@"\??\","")}";
                    }
                    else
                    {
                        reparse = $"Reparse: {subitem.ReparsePoint.SubstituteName.Replace(@"\??\","")}";
                    }
                }

                if (subitem.SubItems.Count > 0)
                {
                    logger.Info($"\t{subitem.Name} (directory) {reparse}");
                }
                else
                {
                    logger.Info($"\t{subitem.Name} (Ads: {subitem.HasAds} Hardlink: {subitem.IsHardLink}) {reparse} file size: 0x{subitem.FileSize:X}");
                }
            }

            foreach (var directory in dir.SubItems.Values.Where(t => t.SubItems.Count > 0))
            {
                DumpFiles(directory);
            }
        }


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

            var m2 = MftFile.Load(xwf);

            m2.BuildFileSystem();

            var logger = LogManager.GetCurrentClassLogger();

            logger.Info(
                $"\r\n\r\nRecord count: {m2.FileRecords.Count:N0} free records: {m2.FreeFileRecords.Count:N0} Bad records: {m2.BadRecords.Count:N0} Uninit records: {m2.UninitializedRecords.Count:N0}");
            
            using (var s = new StreamWriter($@"C:\temp\mft.txt",false,Encoding.Unicode))
            {
                foreach (var f in m2.FileRecords)
                {
                    s.WriteLine(f.Value);
                    //logger.Info(f.Value);

//                    var ads = f.Value.GetAlternateDataStreams();
//
//                    foreach (var adsInfo in ads)
//                    {
//                        logger.Info(adsInfo);
//                    }
                }

                s.Flush();
            }

            using (var s = new StreamWriter($@"C:\temp\mftFree.txt",false,Encoding.Unicode))
            {
                foreach (var f in m2.FreeFileRecords)
                {
                    s.WriteLine(f.Value);
                    //logger.Info(f.Value);

//                    var ads = f.Value.GetAlternateDataStreams();
//
//                    foreach (var adsInfo in ads)
//                    {
//                        logger.Info(adsInfo);
//                    }
                }

                s.Flush();
            }

            DumpFiles(m2.RootDirectory);


            var end = DateTimeOffset.Now;

            var dif = end.Subtract(start).TotalSeconds;

            Debug.WriteLine(dif);
        }
    }
}