using System;
using System.Diagnostics;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;

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

        [Test]
        public void Something()
        {
            var start = DateTimeOffset.Now;

            var m2 = MftFile.Load(xwf);

            m2.BuildFileSystem();

            var logger = LogManager.GetCurrentClassLogger();

            logger.Info(
                $"\r\n\r\nRecord count: {m2.FileRecords.Count:N0} free records: {m2.FreeFileRecords.Count:N0} Bad records: {m2.BadRecords.Count:N0} Uninit records: {m2.UninitializedRecords.Count:N0}");

            using (var s = new StreamWriter($@"C:\temp\mft.txt"))
            {
            
                foreach (var f in m2.FileRecords)
                {
                    s.WriteLine(f.Value);
                    //logger.Info(f.Value);
                }    

                s.Flush();
            }



            var end = DateTimeOffset.Now;

            var dif = end.Subtract(start).TotalSeconds;

            Debug.WriteLine(dif);
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
    }
}