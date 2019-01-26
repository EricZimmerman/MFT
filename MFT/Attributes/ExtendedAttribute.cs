using System;
using System.Collections.Generic;
using System.Diagnostics;
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

//
//                if (name.Equals("LXATTRB"))
//                {
//                    index += 56;
//                }
//
//                if (name.Equals("LXXATTR"))
//                {
//                    index += 56;
//                }
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

                Debug.WriteLine($"name: {name} 0x{bytese.Length:X}");

                index += nameLen;
                index += 1; //null char

                while (index % 8 != 0)
                {
                    index += 1; //get to next 8 byte boundary
                }

                switch (name)
                {
                    case "LXATTRB":
                        var lbBuff = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,lbBuff,0,lbBuff.Length);
                        var lb = new Lxattrb(lbBuff);
                        Debug.WriteLine(lb);
                        break;
                    case "LXXATTR":
                        var lrBuff = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,lrBuff,0,lrBuff.Length);
                        var lr = new Lxattrr(lrBuff);
                        Debug.WriteLine(lr);
                        break;
                    case "$KERNEL.PURGE.ESBCACHE":
                        var kpEs = new byte[bytese.Length - index];
                         Buffer.BlockCopy(bytese,index,kpEs,0,kpEs.Length);
                        //TODO FINISH
                        break;
                    case "$CI.CATALOGHINT":
                        var ciCat = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,ciCat,0,ciCat.Length);
                        //TODO FINISH
                        break;
                    case "$KERNEL.PURGE.APPXFICACHE":
                        var kpAppXFi = new byte[bytese.Length - index];
                        Buffer.BlockCopy(bytese,index,kpAppXFi,0,kpAppXFi.Length);
                        //TODO FINISH
                        break;

                    default:
                        var log = LogManager.GetLogger("EA");
                        log.Debug($"Unknown EA with name: {name}, Length: 0x{(bytese.Length - index):X}");
                        //var defBuff = new byte[bytese.Length - index];
                       // Buffer.BlockCopy(bytese,index,defBuff,0,defBuff.Length);
                    //    File.WriteAllBytes($"C:\\temp\\{name}_{Guid.NewGuid().ToString()}.bin",defBuff);
                        break;
                }
            }


           
        }

        public byte[] Content { get; }

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

            return sb.ToString();
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

         
            index += 1; //unknown

            while (index < rawBytes.Length)
            {
                index += 3; //unknown
                var valueSize = BitConverter.ToInt16(rawBytes, index);
                index += 2;
                var keySize =rawBytes[index];
                index += 1;

                var keyName = Encoding.GetEncoding(1252).GetString(rawBytes, index, keySize);
                index += keySize;
                var valueData = Encoding.GetEncoding(1252).GetString(rawBytes, index, valueSize);
                index += valueSize;

                KeyValues.Add(keyName,valueData);

                while (index % 8 != 0)
                {
                    index += 1; //get to next 8 byte boundary
                }
            }
        }

        public short Format { get; }
        public short Version { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

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

            LastAccessTime = BitConverter.ToInt64(rawBytes,index);
            index += 8;
            ModifiedTime = BitConverter.ToInt64(rawBytes,index);
            index += 8;
            InodeChanged = BitConverter.ToInt64(rawBytes,index);
            index += 8;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"Format: 0x{Format:X}");
            sb.AppendLine($"Version: 0x{Version:X}");
            sb.AppendLine($"Mode: 0x{Mode:X}");
            sb.AppendLine($"Uid/Gid: 0x{Uid:X}/0x{Gid:X}");
            sb.AppendLine($"DeviceId: 0x{DeviceId:X}");

            sb.AppendLine($"LastAccessNanoSeconds: 0x{LastAccessNanoSeconds:X}");
            sb.AppendLine($"ModifiedNanoSeconds: 0x{ModifiedNanoSeconds:X}");
            sb.AppendLine($"InodeChangedNanoSeconds: 0x{InodeChangedNanoSeconds:X}");

            sb.AppendLine($"LastAccessTime: 0x{LastAccessTime:X}");
            sb.AppendLine($"ModifiedTime: 0x{ModifiedTime:X}");
            sb.AppendLine($"InodeChanged: 0x{InodeChanged:X}");

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

        public long LastAccessTime { get; }
        public long ModifiedTime { get; }
        public long InodeChanged { get; }
    }
}