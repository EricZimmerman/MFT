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
        public static string Mft1 = @"..\..\TestFiles\$MFT";
        public static string xwf = @"D:\Code\MFT\MFT.Test\TestFiles\xw\$MFT";
        public static string Mft4 = @"D:\Code\MFT\MFT.Test\TestFiles\NIST\DFR-16\$MFT";
        public static string tdungan = @"D:\Code\MFT\MFT.Test\TestFiles\tdungan\$MFT";
        public static string nromanoff = @"D:\SynologyDrive\MFTs\nromanoff\$MFT";
        public static string nfury = @"D:\SynologyDrive\MFTs\nfury\$MFT";
        public static string CAPUANO = @"D:\SynologyDrive\MFTs\$MFT_FROM_CAPUANO";
        public static string Vanko = @"D:\SynologyDrive\MFTs\vanko\vanko_MFT";


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

            var test = @"D:\SynologyDrive\MFTs\20180615_MFTECmd_Bad_MFT_AMJH";

            var m2 = MftFile.Load(test);

         //   m2.BuildFileSystem();getcur

         //   m2.GetFileRecord("aaa");

            var logger = LogManager.GetCurrentClassLogger();

            logger.Info(
                $"\r\n\r\nRecord count: {m2.FileRecords.Count:N0} free records: {m2.FreeFileRecords.Count:N0} Bad records: {m2.BadRecords.Count:N0} Uninit records: {m2.UninitializedRecords.Count:N0}");


            foreach (var m2FileRecord in m2.FileRecords)
            {
                foreach (var attribute in m2FileRecord.Value.Attributes.Where(t=>t.AttributeType == AttributeType.FileName))
                {
                    var fn = (FileName) attribute;
                    if (fn.FileInfo.NameType == NameTypes.Dos)
                    {
                        continue;
                    }

                    Debug.WriteLine($"{m2FileRecord.Value.EntryNumber},{m2FileRecord.Value.SequenceNumber},\"{m2.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey())}\\{fn.FileInfo.FileName}\",InUse,{m2FileRecord.Value.IsDirectory()}");          
                }
            }

            foreach (var m2FileRecord in m2.FreeFileRecords)
            {
                foreach (var attribute in m2FileRecord.Value.Attributes.Where(t=>t.AttributeType == AttributeType.FileName))
                {

                    var fn = (FileName) attribute;
                    if (fn.FileInfo.NameType == NameTypes.Dos)
                    {
                        continue;
                    }

                    Debug.WriteLine($"{m2FileRecord.Value.EntryNumber},{m2FileRecord.Value.SequenceNumber},\"{m2.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey())}\\{fn.FileInfo.FileName}\",Free,{m2FileRecord.Value.IsDirectory()}");          
                }
            }

//            using (var s = new StreamWriter($@"C:\temp\mft.txt", false, Encoding.Unicode))
//            {
//                foreach (var f in m2.FileRecords)
//                {
//                    s.WriteLine(f.Value);
//                    //logger.Info(f.Value);
//
////                    var ads = f.Value.GetAlternateDataStreams();
////
////                    foreach (var adsInfo in ads)
////                    {
////                        logger.Info(adsInfo);
////                    }
//                }
//
//                s.Flush();
//            }
//
//            using (var s = new StreamWriter($@"C:\temp\mftFree.txt", false, Encoding.Unicode))
//            {
//                foreach (var f in m2.FreeFileRecords)
//                {
//                    s.WriteLine(f.Value);
//                    //  logger.Info(f.Value);
//
////                    var ads = f.Value.GetAlternateDataStreams();
////
////                    foreach (var adsInfo in ads)
////                    {
////                        logger.Info(adsInfo);
////                    }
//                }
//
//                s.Flush();
//            }

          //  DumpFiles(m2.RootDirectory);

//              //XWF tests
//            //file test, existing
//            key = "0000005F-00000002"; //\Documents and Settings\EdgarAllanPoe\My Documents\My Pictures\smallpic.jpg
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//
//            //file test, deleted 
//            key = "00000270-00000001"; //\Documents and Settings\EdgarAllanPoe\My Documents\My Pictures\Dog.gif
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//
//            //file test, existing //0000004D-00000002 == Trash
//            key = "0000004D-00000002"; //\Documents and Settings\EdgarAllanPoe\My Documents\Trash
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//            
//            //dir test, existing
//            key = "00000196-00000003"; //\Docs\Pictures
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//            
//            //dir test, deleted
//            
//            key = "0000021C-00000001"; //\Documents and Settings\EdgarAllanPoe\Local Settings\Temp\Temporary Internet Files\Content.IE5\M3ILGGNU
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");
//           
//
//
//            key = "0000F8B3-00000001"; //\Documents and Settings\EdgarAllanPoe\Local Settings\Temp\Temporary Internet Files\Content.IE5\M3ILGGNU
//            fr = GetFileRecord(key);
//            map = GetMap(fr);
//            path = GetFullPathFromMap(map);
//            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");

/*            //tdungan
            key = "0009322-00000003"; //\Program Files\Mozilla Firefox\extensions\{CAFEEFAC-0016-0000-0031-ABCDEFFEDCBA}\chrome\content\ffjcext\ffjcext.xul
            fr = GetFileRecord(key);
            map = GetMap(fr);
            path = GetFullPathFromMap(map);
            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");

            key = "00006E5E-00000004"; //\Program Files\Mozilla Firefox\extensions\{CAFEEFAC-0016-0000-0031-ABCDEFFEDCBA}\chrome\content\ffjcext\ffjcext.xul
            fr = GetFileRecord(key);
            map = GetMap(fr);
            path = GetFullPathFromMap(map);
            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");

            key = "00009341-00000004"; //\Program Files\Mozilla Firefox\extensions\{CAFEEFAC-0016-0000-0031-ABCDEFFEDCBA}\chrome\locale\zh-TW\ffjcext
            fr = GetFileRecord(key);
            map = GetMap(fr);
            path = GetFullPathFromMap(map);
            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");


            key = "00006214-00000001"; //\Program Files\Microsoft Silverlight\4.0.60531.0\pt-BR\mscorrc.dll
            fr = GetFileRecord(key);
            map = GetMap(fr);
            path = GetFullPathFromMap(map);
            _logger.Info($"TEST: {fr.GetFileNameAttributeFromFileRecord().FileInfo.FileName} with key {key} ==> {path}");*/


            var end = DateTimeOffset.Now;

            var dif = end.Subtract(start).TotalSeconds;

            Debug.WriteLine(dif);
        }
    }
}