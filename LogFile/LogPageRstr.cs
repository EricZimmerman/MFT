using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog;

namespace LogFile;

public enum RestartFlag
{
    None = 0x0,
    OneByOne = 0x1,
    NewArea = 0x2
}

public class LogPageRstr
{
    private const int RstrSig = 0x52545352;
    private const int ChkdSig = 0x52545351;
    public long CheckDiskLsn;
    public short ClientArrayOffset;
    public short ClientFreeList;
    public short ClientInUseList;
    public long CurrentLsn;
    public RestartFlag Flags;
    public int LastLsnDataLen;
    public short LogClientCount;
    public long LogFileSize;
    public short LogPageDataOffset;
    public int LogPageSize;
    public short MajorFormatVersion;
    public short MinorFormatVersion;
    public short RecordHeaderLen;
    public short RestartAreaLen;
    public short RestartOffset;
    public int RevisionNumber;
    public int SeqNumBits;
    public int SystemPageSize;

    public LogPageRstr(byte[] rawBytes, int offset)
    {
        var index = 0x0;

        var sigCheck = BitConverter.ToInt32(rawBytes, index);

        if (sigCheck != RstrSig && sigCheck != ChkdSig)
        {
            throw new Exception("Invalid signature! Expected 'RSTR|CHKD' signature.");
        }

        Offset = offset;

        index += 4;

        var fixupOffset = BitConverter.ToInt16(rawBytes, index);
        index += 2;
        var numFixupPairs = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        CheckDiskLsn = BitConverter.ToInt64(rawBytes, index);
        index += 8;
        SystemPageSize = BitConverter.ToInt32(rawBytes, index);
        index += 4;

        LogPageSize = BitConverter.ToInt32(rawBytes, index);
        index += 4;

        RestartOffset = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        MinorFormatVersion = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        MajorFormatVersion = BitConverter.ToInt16(rawBytes, index);
        index += 2;


        var fixupTotalLength = numFixupPairs * 2;

        var fixupBuffer = new byte[fixupTotalLength];
        Buffer.BlockCopy(rawBytes, fixupOffset, fixupBuffer, 0, fixupTotalLength);

        var fixupData = new FixupData(fixupBuffer);

        var fixupOk = true;

        //fixup verification
        var counter = 512;
        foreach (var bytese in fixupData.FixupActual)
        {
            //adjust the offset to where we need to check
            var fixupOffset1 = counter - 2;

            var expected = BitConverter.ToInt16(rawBytes, fixupOffset1);
            if (expected != fixupData.FixupExpected)
            {
                fixupOk = false;
                Log.Warning(
                    "Fixup values do not match at 0x{FixupOffset1:X}. Expected: 0x{FixupExpected:X2}, actual: 0x{Expected:X2}",
                    fixupOffset1, fixupData.FixupExpected, expected);
            }

            //replace fixup expected with actual bytes. bytese has actual replacement values in it.
            Buffer.BlockCopy(bytese, 0, rawBytes, fixupOffset1, 2);

            counter += 512;
        }

        index += fixupTotalLength;

        while (index % 8 != 0)
        {
            index += 1;
        }

        CurrentLsn = BitConverter.ToInt64(rawBytes, index);
        index += 8;
        LogClientCount = BitConverter.ToInt16(rawBytes, index);
        index += 2;
        ClientFreeList = BitConverter.ToInt16(rawBytes, index);
        index += 2;
        ClientInUseList = BitConverter.ToInt16(rawBytes, index);
        index += 2;
        Flags = (RestartFlag)BitConverter.ToInt16(rawBytes, index);
        index += 2;
        SeqNumBits = BitConverter.ToInt32(rawBytes, index);
        index += 4;
        RestartAreaLen = BitConverter.ToInt16(rawBytes, index);
        index += 2;
        ClientArrayOffset = BitConverter.ToInt16(rawBytes, index);
        index += 2;
        LogFileSize = BitConverter.ToInt64(rawBytes, index);
        index += 8;
        LastLsnDataLen = BitConverter.ToInt32(rawBytes, index);
        index += 4;
        RecordHeaderLen = BitConverter.ToInt16(rawBytes, index);
        index += 2;
        LogPageDataOffset = BitConverter.ToInt16(rawBytes, index);
        index += 2;
        RevisionNumber = BitConverter.ToInt32(rawBytes, index);

        index = 0x30 + ClientArrayOffset;
        ClientRecords = new List<ClientRecord>();

        for (var i = 0; i < LogClientCount; i++)
        {
            var buff = new byte[160]; //len of clientRecord

            Buffer.BlockCopy(rawBytes, index, buff, 0, 160);

            var cr = new ClientRecord(buff);
            ClientRecords.Add(cr);
            index += 160;
        }
    }

    public int Offset { get; }

    public List<ClientRecord> ClientRecords { get; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append($"checkDiskLsn: 0x{CheckDiskLsn:X} ");
        sb.Append($"systemPageSize: 0x{SystemPageSize:X} ");
        sb.Append($"logPageSize: 0x{LogPageSize:X} ");
        sb.Append($"restartOffset: 0x{RestartOffset:X} ");
        sb.Append($"majorFormatVersion: 0x{MajorFormatVersion:X} ");
        sb.Append($"minorFormatVersion: 0x{MinorFormatVersion:X} ");
        sb.Append($"currentLsn: 0x{CurrentLsn:X} ");
        sb.Append($"logClient: 0x{LogClientCount:X} ");
        sb.Append($"ClientFreeList: 0x{ClientFreeList:X} ");
        sb.Append($"ClientInUseList: 0x{ClientInUseList:X} ");
        sb.Append($"flags: {Flags} ");
        sb.Append($"SeqNumBits: 0x{SeqNumBits:X} ");
        sb.Append($"RestartAreaLen: 0x{RestartAreaLen:X} ");
        sb.Append($"ClientArrayOffset: 0x{ClientArrayOffset:X} ");
        sb.Append($"LogFileSize: 0x{LogFileSize:X} ");
        sb.Append($"lastLsnDataLen: 0x{LastLsnDataLen:X} ");
        sb.Append($"recordHeaderLen: 0x{RecordHeaderLen:X} ");
        sb.Append($"LogPageDataOffset: 0x{LogPageDataOffset:X} ");
        sb.Append($"revisionNumber: 0x{RevisionNumber:X} ");
        sb.AppendLine();

        sb.AppendLine($"Client Records ({ClientRecords.Count:N0})");
        foreach (var clientRecord in ClientRecords)
        {
            sb.AppendLine(clientRecord.ToString());
        }

        sb.AppendLine();

        return sb.ToString();
    }
}

public class ClientRecord
{
    public ClientRecord(byte[] rawBytes)
    {
        var br = new BinaryReader(new MemoryStream(rawBytes));

        OldestLsn = br.ReadInt64();
        ClientRestartLsn = br.ReadInt64();
        PrevClient = br.ReadInt16();
        NextClient = br.ReadInt16();
        SeqNumber = br.ReadInt16();
        br.ReadBytes(6); //skip
        var clientNameLen = br.ReadInt32();

        ClientName = Encoding.Unicode.GetString(br.ReadBytes(clientNameLen));
    }

    public long OldestLsn { get; }
    public long ClientRestartLsn { get; }
    public short PrevClient { get; }
    public short NextClient { get; }
    public short SeqNumber { get; }
    public string ClientName { get; }

    public override string ToString()
    {
        return
            $"  oldestLsn: 0x{OldestLsn:X} clientRestartLsn: 0x{ClientRestartLsn:X} prevClient: 0x{PrevClient:X} nextClient: 0x{NextClient:X} seqNumber: 0x{SeqNumber:X} ClientName: {ClientName}";
    }
}