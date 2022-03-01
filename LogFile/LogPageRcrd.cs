using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace LogFile;

public enum PageRecordFlag
{
    MultiplePages = 0x1,
    NoRedo = 0x2,
    NoUndo = 0x4
}

public class LogPageRcrd
{
    private const int RcrdSig = 0x44524352;

    public LogPageRcrd(byte[] rawBytes, int offset)
    {
        var index = 0x0;
        var sigCheck = BitConverter.ToInt32(rawBytes, index);

        if (sigCheck != RcrdSig)
        {
            throw new Exception("Invalid signature! Expected 'RCRD' signature.");
        }

        Offset = offset;

        index += 4;

        var fixupOffset = BitConverter.ToInt16(rawBytes, index);
        index += 2;
        var numFixupPairs = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        LastLogFileSequenceNumber = BitConverter.ToInt64(rawBytes, index);
        index += 8;
        Flags = BitConverter.ToInt32(rawBytes, index);
        index += 4;

        PageCount = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        PagePosition = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        FreeSpaceOffset = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        var wordAlign = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        var dwordAlign = BitConverter.ToInt32(rawBytes, index);
        index += 4;

        LastEndLogFileSequenceNumber = BitConverter.ToInt64(rawBytes, index);
        index += 8;

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
                    "Fixup values do not match at 0x{FixupOffset:X}. Expected: 0x{FixupExpected:X2}, actual: 0x{Expected:X2}",
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

        //header is 0x58 bytes, so go past it

        // index = 0x58;

        Log.Information(
            "   LastLogFileSequenceNumber: 0x{LastLogFileSequenceNumber:X} Flags: {Flags} PageCount: 0x{PageCount:X} PagePosition: 0x{PagePosition:X} Free space offset: 0x{FreeSpaceOffset:X} " +
            "LastEndLogFileSequenceNumber: 0x{LastEndLogFileSequenceNumber:X} " +
            "LastLogFileSequenceNumber==LastEndLogFileSequenceNumber: {LastEndLogFileSequenceNumber==LastLogFileSequenceNumber}",
            LastLogFileSequenceNumber, Flags, PageCount, PagePosition, FreeSpaceOffset, LastEndLogFileSequenceNumber,
            LastEndLogFileSequenceNumber == LastLogFileSequenceNumber);

        //record is 0x30 + clientDatalen long


        Records = new List<Record>();

        while (index < rawBytes.Length)
        {
            var so = index;
            var thisLsn = BitConverter.ToInt64(rawBytes, index);
            var prevLsn = BitConverter.ToInt64(rawBytes, index + 8);
            var clientUndoLsn = BitConverter.ToInt64(rawBytes, index + 16);

            //     _logger.Info($"     this: {thisLsn:X} prev: {prevLsn:X} undo: {clientUndoLsn:X}");
            var clientDataLen = BitConverter.ToInt32(rawBytes, index + 24);
            var buff = new byte[clientDataLen + 0x30];
            Buffer.BlockCopy(rawBytes, index, buff, 0, buff.Length);

            var rec = new Record(buff);

            Records.Add(rec);

            index += buff.Length;

            Log.Information("     Record: {Record}", rec);

            if (thisLsn == LastEndLogFileSequenceNumber)
            {
                Log.Warning("At last LSN in this page (0x{ThisLsn:X}). Found it at offset 0x{So:X}\r\n", thisLsn, so);
                break;
            }
        }

        //Debug.WriteLine($"at abs offset: 0x{(offset+index):X}, RCRD Offset: 0x{Offset:X}");
    }

    public long LastLogFileSequenceNumber { get; }
    public int Flags { get; }
    public short PageCount { get; }
    public short PagePosition { get; }
    public short FreeSpaceOffset { get; }
    public long LastEndLogFileSequenceNumber { get; }

    public int Offset { get; }

    public List<Record> Records { get; }
}

public enum RecTypeFlag
{
    RestRecord = 0x1,
    CheckPointRecord = 0x02
}

public enum RecordHeaderFlag
{
    ClientRecord = 0x1,
    ClientRestartArea = 0x2
}

public enum OpCode
{
    Noop = 0x00,
    CompensationlogRecord = 0x01,
    InitializeFileRecordSegment = 0x02,
    DeallocateFileRecordSegment = 0x03,
    WriteEndofFileRecordSegement = 0x04,
    CreateAttribute = 0x05,
    DeleteAttribute = 0x06,
    UpdateResidentValue = 0x07,
    UpdataeNonResidentValue = 0x08,
    UpdateMappingPairs = 0x09,
    DeleteDirtyClusters = 0x0A,
    SetNewAttributeSizes = 0x0B,
    AddIndexEntryRoot = 0x0C,
    DeleteIndexEntryRoot = 0x0D,
    AddIndexEntryAllocation = 0x0E,
    DeleteIndexEntryAllocation = 0x0F,
    WriteEndOfIndexBuffer = 0x10,
    SetIndexEntryVcnRoot = 0x11,
    SetIndexEntryVcnAllocation = 0x12,
    UpdateFileNameRoot = 0x13,
    UpdateFileNameAllocation = 0x14,
    SetBitsInNonresidentBitMap = 0x15,
    ClearBitsInNonresidentBitMap = 0x16,
    HotFix = 0x17,
    EndTopLevelAction = 0x18,
    PrepareTransaction = 0x19,
    CommitTransaction = 0x1A,
    ForgetTransaction = 0x1B,
    OpenNonresidentAttribute = 0x1C,
    OpenAttributeTableDump = 0x1D,
    AttributeNamesDump = 0x1E,
    DirtyPageTableDump = 0x1F,
    TransactionTableDump = 0x20,
    UpdateRecordDataRoot = 0x21,
    UpdateRecordDataAllocation = 0x22,
    UpdateRelativeDataIndex = 0x23,
    UpdateRelativeDataAllocation = 0x24,
    ZeroEndOfFileRecord = 0x25
}

public class Record
{
    public Record(byte[] rawBytes)
    {
        var br = new BinaryReader(new MemoryStream(rawBytes));

        ThisLsn = br.ReadInt64();
        PreviousLsn = br.ReadInt64();
        UndoLsn = br.ReadInt64();

        DataLength = br.ReadInt32();
        ClientId = br.ReadInt32();
        RecordType = (RecTypeFlag)br.ReadInt32();
        TransactionId = br.ReadInt32();

        Flags = (RecordHeaderFlag)br.ReadInt16();
        RedoOpCode = (OpCode)br.ReadInt16();
        UndoOpCode = (OpCode)br.ReadInt16();
        RedoOffset = br.ReadInt16();
        RedoLength = br.ReadInt16();
        UndoOffset = br.ReadInt16();
        UndoLength = br.ReadInt16();
        TargetAtrribute = br.ReadInt16();
        LcnToFollow = br.ReadInt16();
        RecordOffset = br.ReadInt16();
        AttributeOffset = br.ReadInt16();
        ClusterBlockOffset = br.ReadInt16();
        TargetblockSize = br.ReadInt16();

        TargetVcn = br.ReadInt64();

//          var l = LogManager.GetCurrentClassLogger();
//              l.Info($"This LSN: 0x{br.ReadInt64():X}"); //this
//              l.Info($"prev LSN: 0x{br.ReadInt64():X}"); //this
//              l.Info($"undo LSN: 0x{br.ReadInt64():X}"); //this
//              
//              l.Info($"data len: 0x{br.ReadInt32():X}"); //this
//              l.Info($"clientid: 0x{br.ReadInt32():X}"); //this
//              l.Info($"rec type: 0x{br.ReadInt32():X}"); //this
//              l.Info($"trans id: 0x{br.ReadInt32():X}"); //this
//
//              l.Info($"flags: 0x{br.ReadInt16():X}"); //this
//
//              br.ReadBytes(6); //reserved
//
//              l.Info($"redo op: 0x{br.ReadInt16():X}"); //this
//              l.Info($"undo op: 0x{br.ReadInt16():X}"); //this
//              l.Info($"redo offset: 0x{br.ReadInt16():X}"); //this
//              l.Info($"redo len:0x {br.ReadInt16():X}"); //this
//              l.Info($"undo offset: 0x{br.ReadInt16():X}"); //this
//              l.Info($"undo len: 0x{br.ReadInt16():X}"); //this
//              l.Info($"target attr: 0x{br.ReadInt16():X}"); //this
//              l.Info($"LCN to follow: 0x{br.ReadInt16():X}"); //this
//              l.Info($"record offset: 0x{br.ReadInt16():X}"); //this
//              l.Info($"attr offset: 0x{br.ReadInt16():X}"); //this
//              l.Info($"clusterblockOffset 0x{br.ReadInt16():X}"); //this
//              l.Info($"TargetblockSize 0x{br.ReadInt16():X}"); //this
//            
//            
//              l.Info($"target vcn: 0x{br.ReadInt64():X}"); //this

        //lcns are here

        // var ClusterNums = new List<long>();
        //
        // for (int i = 0; i < LcnToFollow; i++)
        // {
        //     var lc = br.ReadInt64();
        //     ClusterNums.Add(lc);
        // }
    }

    public long ThisLsn { get; }
    public long PreviousLsn { get; }
    public long UndoLsn { get; }

    public int DataLength { get; }
    public int ClientId { get; }
    public RecTypeFlag RecordType { get; }
    public int TransactionId { get; }
    public RecordHeaderFlag Flags { get; }
    public OpCode RedoOpCode { get; }
    public OpCode UndoOpCode { get; }
    public short RedoOffset { get; }
    public short RedoLength { get; }
    public short UndoOffset { get; }
    public short UndoLength { get; }
    public short TargetAtrribute { get; }
    public short LcnToFollow { get; }
    public short RecordOffset { get; }
    public short AttributeOffset { get; }
    public short ClusterBlockOffset { get; }
    public short TargetblockSize { get; }
    public long TargetVcn { get; }

    public override string ToString()
    {
        return
            $"ThisLsn: 0x{ThisLsn:X} PrevLsn: 0x{PreviousLsn:X} UndoLsn: 0x{UndoLsn:X} size: 0x{DataLength:X} Client id: 0x{ClientId:X} Rec type: {RecordType} TransId: 0x{TransactionId:X} Flags: {Flags} Redo code: {RedoOpCode} Undo code: {UndoOpCode} redo offset: 0x{RedoOffset:X} redo len: 0x{RedoLength:X} undo offset: 0x{UndoOffset:X} undo len: 0x{UndoLength:X} target attr: 0x{TargetAtrribute:X} LsnToFollow: 0x{LcnToFollow:X} RecordOffset: 0x{RecordOffset:X} attr offset: 0x{AttributeOffset:X} cluster block offset: 0x{ClusterBlockOffset:X} target block size: 0x{TargetblockSize:X} target vcn: 0x{TargetVcn:X}";
    }
}