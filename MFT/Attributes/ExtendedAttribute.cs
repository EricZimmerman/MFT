using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using NLog;

namespace MFT.Attributes
{
    public class ExtendedAttribute : Attribute
    {
        public ExtendedAttribute(byte[] rawBytes) : base(rawBytes)
        {
            Content = new byte[AttributeContentLength];

            Buffer.BlockCopy(rawBytes, ContentOffset, Content, 0, AttributeContentLength);

            SubItems = new List<object>();

            ProcessContent();
        }

        private void ProcessContent()
        {
            if (Content.Length == 0)
            {
                return;
            }

            var index = 0;

            var chunks = new List<Byte[]>();

            while (index < Content.Length)
            {
                var size = BitConverter.ToInt32(Content, index);

                var buff = new byte[size];
                Buffer.BlockCopy(Content,index,buff,0,size);
                chunks.Add(buff);
                index += size;
            }

            foreach (var bytese in chunks)
            {
                index = 0;

                var nextOffset = BitConverter.ToUInt32(bytese, index);
                if (nextOffset == 0)
                {
                    break;
                }
                index += 4;
                var flags = bytese[index];
                index += 1;
                var nameLen = bytese[index];
                index += 1;
                var eaLen = BitConverter.ToUInt16(bytese, index);
                index += 2;

                var name = Encoding.GetEncoding(1252).GetString(bytese, index, nameLen);

                index += nameLen;
                index += 1; //null char

//                var defBuff = new byte[bytese.Length - index];
//                 Buffer.BlockCopy(bytese,index,defBuff,0,defBuff.Length);
//                    File.WriteAllBytes($"D:\\Temp\\Maxim_EA)STUFF_MFT_wsl2\\EASAmples\\{name}_{Guid.NewGuid().ToString()}.bin",defBuff);

                switch (name)
                {
                    case ".LONGNAME":
                        var lnBuff = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,lnBuff,0,lnBuff.Length);
                        var ln = new LongName(lnBuff);
                        SubItems.Add(ln);
                        break;
                    case "LXATTRB":
                        var lbBuff = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,lbBuff,0,lbBuff.Length);
                        var lb = new Lxattrb(lbBuff);
                        SubItems.Add(lb);
                        //Debug.WriteLine(lb);
                        break;
                    case "LXXATTR":
                        var lrBuff = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,lrBuff,0,lrBuff.Length);
                        var lr = new Lxattrr(lrBuff);
                        SubItems.Add(lr);
                        //Debug.WriteLine(lr);
                        break;
                    case "$KERNEL.PURGE.ESBCACHE":
                        var kpEs = new byte[bytese.Length - index];
                         Buffer.BlockCopy(bytese,index,kpEs,0,kpEs.Length);
                        var esbCache = new PurgeEsbCache(kpEs);
                        SubItems.Add(esbCache);
                        //Debug.WriteLine(esbCache);
                        //TODO FINISH
                        break;
                    case "$CI.CATALOGHINT":
                        var ciCat = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,ciCat,0,ciCat.Length);
                        var catHint = new CatHint(ciCat);
                        SubItems.Add(catHint);
                        break;
                    case "$KERNEL.PURGE.APPXFICACHE":
                        var kpAppXFi = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,kpAppXFi,0,kpAppXFi.Length);
                        var appFix = new AppFixCache(kpAppXFi);
                        SubItems.Add(appFix);
                        break;
                    case ".CLASSINFO":
                        var clInfo = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,clInfo,0,clInfo.Length);
                        var clI = new ClassInfo(clInfo);
                        SubItems.Add(clI);
                        break;

                    default:
                        var log = LogManager.GetLogger("EA");
                        log.Warn($"Unknown EA with name: {name}, Length: 0x{(bytese.Length - index):X}");
                        //var defBuff = new byte[bytese.Length - index];
                       // Buffer.BlockCopy(bytese,index,defBuff,0,defBuff.Length);
                    //    File.WriteAllBytes($"D:\\Temp\\Maxim_EA)STUFF_MFT_wsl2\\EASAmples\\{name}_{Guid.NewGuid().ToString()}.bin",defBuff);
                        break;
                }
            }
        }

        public byte[] Content { get; }

        public List<object> SubItems { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** EXTENDED ATTRIBUTE ****");

            sb.AppendLine(base.ToString());

            var asAscii = Encoding.GetEncoding(1252).GetString(Content);
            var asUnicode = Encoding.Unicode.GetString(Content);

            sb.AppendLine();
            sb.AppendLine(
                $"Extended Attribute:: {BitConverter.ToString(Content)}\r\n\r\nASCII: {asAscii}\r\nUnicode: {asUnicode}");

            if (SubItems.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Sub items");
            }

            foreach (var subItem in SubItems)
            {
                sb.AppendLine(subItem.ToString());
            }

            return sb.ToString();
        }
    }

    public class LongName
    {
        public LongName(byte[] rawBytes)
        {
            var index = 0;

            index += 2; // unknown

            var size = BitConverter.ToInt16(rawBytes, index);
            index += 2;

            Name = Encoding.GetEncoding(1252).GetString(rawBytes, index, size);
        }


        public string Name { get; }

        public override string ToString()
        {
            return $".LONGNAME: {Name}";
        }
    }

    public class ClassInfo
    {
        public ClassInfo(byte[] rawBytes)
        {
//            var index = 0;
//
//            index += 2; // unknown
//
//            var size = BitConverter.ToInt16(rawBytes, index);
//            index += 2;
//
//            Name = Encoding.GetEncoding(1252).GetString(rawBytes, index, size);
        }


        public string Name { get; }

        public override string ToString()
        {
            return $".ClassInfo: Not decoded";
        }
    }

    public class CatHint
    {
        public CatHint(byte[] rawBytes)
        {
            var index = 0;

            Format = BitConverter.ToInt16(rawBytes,index);
            index += 2;

            var size = BitConverter.ToInt16(rawBytes, index);
            index += 2;

            Hint = Encoding.GetEncoding(1252).GetString(rawBytes, index, size);
        }

        public short Format { get; }

        public string Hint { get; }

        public override string ToString()
        {
            return $"$CI.CATALOGHINT | Hint: {Hint}";
        }
    }

    public class AppFixCache
    {
        public AppFixCache(byte[] rawBytes)
        {
            var tsraw = BitConverter.ToInt64(rawBytes, 0);
            if (tsraw < 0)
            {
                tsraw = 0;
            }

            Timestamp = DateTimeOffset.FromFileTime(tsraw).ToUniversalTime();
            
            RemainingBytes = new byte[rawBytes.Length-8];
            Buffer.BlockCopy(rawBytes,8,RemainingBytes,0,RemainingBytes.Length);
        }

        public DateTimeOffset Timestamp { get; }
        public byte[] RemainingBytes { get; }

        public override string ToString()
        {
            return $"$KERNEL.PURGE.APPXFICACHE | Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss.fffffff} Remaining bytes: {BitConverter.ToString(RemainingBytes)}";
        }
    }

    public class PurgeEsbCache
    {
        public PurgeEsbCache(byte[] rawBytes)
        {
            var index = 8;
            Timestamp = DateTimeOffset.FromFileTime(BitConverter.ToInt64(rawBytes, index)).ToUniversalTime();
            index += 8;
            Timestamp2 = DateTimeOffset.FromFileTime(BitConverter.ToInt64(rawBytes, index)).ToUniversalTime();
            index += 8;
        }


        public DateTimeOffset Timestamp { get; }
        public DateTimeOffset Timestamp2 { get; }

        public override string ToString()
        {
            return $"$KERNEL.PURGE.ESBCACHE | Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss.fffffff} Timestamp2: {Timestamp2:yyyy-MM-dd HH:mm:ss.fffffff}";
        }
    }

    public class Lxattrr
    {
        public Dictionary<string,string> KeyValues { get; }
        public Lxattrr(byte[] rawBytes)
        {
            KeyValues = new Dictionary<string, string>();

            var index = 0;

            Format = BitConverter.ToInt16(rawBytes,index);
            index += 2;
            Version = BitConverter.ToInt16(rawBytes,index);
            index += 2;
         
            while (index < rawBytes.Length)
            {
                var offsetToNextRecord = BitConverter.ToInt32(rawBytes, index);
                index += 4;
                //index += 3; //unknown
                var valueSize = BitConverter.ToInt16(rawBytes, index);
                index += 2;
                var keySize =rawBytes[index];
                index += 1;

                var keyName = Encoding.GetEncoding(1252).GetString(rawBytes, index, keySize);
                index += keySize;
                var valueData = Encoding.GetEncoding(1252).GetString(rawBytes, index, valueSize);
                index += valueSize;

                index += 1;//null terminator

                KeyValues.Add(keyName,valueData);

                if (offsetToNextRecord == 0)
                {
                    //we are out of data
                    break;
                }

            }
        }

        public short Format { get; }
        public short Version { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("LXXATTR");

            foreach (var keyValue in KeyValues)
            {
                sb.AppendLine($"Key: {keyValue.Key} --> {keyValue.Value}");
            }

            return sb.ToString();
        }
    }

    public class Lxattrb
    {
        public Lxattrb(byte[] rawBytes)
        {
            var index = 0;
            Format = BitConverter.ToInt16(rawBytes,index);
            index += 2;
            Version = BitConverter.ToInt16(rawBytes,index);
            index += 2;
            Mode = BitConverter.ToInt32(rawBytes,index);
            index += 4;
            Uid = BitConverter.ToInt32(rawBytes,index);
            index += 4;
            Gid = BitConverter.ToInt32(rawBytes,index);
            index += 4;
            DeviceId = BitConverter.ToInt32(rawBytes,index);
            index += 4;
            LastAccessNanoSeconds = BitConverter.ToInt32(rawBytes,index);
            index += 4;
            ModifiedNanoSeconds = BitConverter.ToInt32(rawBytes,index);
            index += 4;
            InodeChangedNanoSeconds = BitConverter.ToInt32(rawBytes,index);
            index += 4;

            LastAccessTime = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(rawBytes,index)).ToUniversalTime();
            index += 8;
            ModifiedTime = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(rawBytes,index)).ToUniversalTime();
            index += 8;
            InodeChanged = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(rawBytes,index)).ToUniversalTime();
            index += 8;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("LXATTRB");
            
            sb.AppendLine($"Format: 0x{Format:X}");
            sb.AppendLine($"Version: 0x{Version:X}");
            sb.AppendLine($"Mode: 0x{Mode:X}");
            sb.AppendLine($"Uid/Gid: 0x{Uid:X}/0x{Gid:X}");
            sb.AppendLine($"Device Id: 0x{DeviceId:X}");

            //convert to seconds so we can use it later.
            //.net has no API for adding nanoseconds that works, so this is what we get
            var lastAccessSubSec = (LastAccessNanoSeconds / 1e+9).ToString(CultureInfo.InvariantCulture);
            var modifiedSubsec = (ModifiedNanoSeconds / 1e+9).ToString(CultureInfo.InvariantCulture);
            var inodeChangeSubsec = (InodeChangedNanoSeconds / 1e+9).ToString(CultureInfo.InvariantCulture);

            sb.AppendLine($"Last Access Time: {LastAccessTime.ToUniversalTime():yyyy-MM-dd HH:mm:ss}.{(lastAccessSubSec.Length>2 ? lastAccessSubSec.Substring(2) : "0000000")}");
            sb.AppendLine($"Modified Time: {ModifiedTime.ToUniversalTime():yyyy-MM-dd HH:mm:ss}.{modifiedSubsec.Substring(2)}");
            sb.AppendLine($"Inode Changed: {InodeChanged.ToUniversalTime():yyyy-MM-dd HH:mm:ss}.{inodeChangeSubsec.Substring(2)}");

            return sb.ToString();
        }

        public short Format { get; }
        public short Version { get; }
        public int Mode { get; }
        public int Uid { get; }
        public int Gid { get; }
        public int DeviceId { get; }

        public int LastAccessNanoSeconds { get; }
        public int ModifiedNanoSeconds { get; }
        public int InodeChangedNanoSeconds { get; }

        public DateTimeOffset LastAccessTime { get; }
        public DateTimeOffset ModifiedTime { get; }
        public DateTimeOffset InodeChanged { get; }
    }
}