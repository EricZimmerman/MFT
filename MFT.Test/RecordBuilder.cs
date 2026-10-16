using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MFT.Test;

/// <summary>Builds FILE records (1024 bytes, 2 sectors with fixups) and their attributes</summary>
public static class RecordBuilder
{
    public const int RecordSize = 1024;
    public const ushort UpdateSequence = 0x0007;

    public static long FileTime(DateTime utc) => utc.ToFileTimeUtc();

    /// <param name="entry">Entry number at 0x2C</param>
    /// <param name="flags">1 in use, 2 directory</param>
    /// <param name="attributes">Each a complete attribute (header and content)</param>
    /// <param name="usedSize">Override the used size at 0x18</param>
    /// <param name="baseRecord">MFT reference of the base record (extension records)</param>
    public static byte[] Record(uint entry, ushort flags, IEnumerable<byte[]> attributes, int? usedSize = null,
        ushort sequence = 1, ulong baseRecord = 0, bool applyFixups = true)
    {
        var r = new byte[RecordSize];
        Encoding.ASCII.GetBytes("FILE").CopyTo(r, 0);
        BitConverter.GetBytes((ushort)0x30).CopyTo(r, 4); //fixup array offset
        BitConverter.GetBytes((ushort)3).CopyTo(r, 6); //usn + one per sector
        BitConverter.GetBytes(0x1234L).CopyTo(r, 8); //$LogFile sequence number
        BitConverter.GetBytes(sequence).CopyTo(r, 0x10);
        BitConverter.GetBytes((ushort)1).CopyTo(r, 0x12); //hard links
        BitConverter.GetBytes((ushort)0x38).CopyTo(r, 0x14); //first attribute
        BitConverter.GetBytes(flags).CopyTo(r, 0x16);
        BitConverter.GetBytes(RecordSize).CopyTo(r, 0x1C);
        BitConverter.GetBytes(baseRecord).CopyTo(r, 0x20);
        BitConverter.GetBytes(entry).CopyTo(r, 0x2C);

        var index = 0x38;
        foreach (var a in attributes)
        {
            a.CopyTo(r, index);
            index += a.Length;
        }

        BitConverter.GetBytes(-1).CopyTo(r, index); //end marker
        index += 8;
        BitConverter.GetBytes(usedSize ?? index).CopyTo(r, 0x18);

        if (applyFixups)
        {
            //the last 2 bytes of each sector go in the fixup array; the update sequence number replaces them
            BitConverter.GetBytes(UpdateSequence).CopyTo(r, 0x30);
            for (var sector = 1; sector <= 2; sector++)
            {
                var end = sector * 512 - 2;
                Buffer.BlockCopy(r, end, r, 0x30 + sector * 2, 2);
                BitConverter.GetBytes(UpdateSequence).CopyTo(r, end);
            }
        }

        return r;
    }

    /// <summary>A resident attribute: 0x18 byte header (+ name), then the content, 8 byte aligned</summary>
    public static byte[] Resident(int type, byte[] content, string name = null, ushort id = 0)
    {
        var nameBytes = name == null ? Array.Empty<byte>() : Encoding.Unicode.GetBytes(name);
        var contentOffset = Align(0x18 + nameBytes.Length);
        var a = new byte[Align(contentOffset + content.Length)];
        BitConverter.GetBytes(type).CopyTo(a, 0);
        BitConverter.GetBytes(a.Length).CopyTo(a, 4);
        a[8] = 0; //resident
        a[9] = (byte)(nameBytes.Length / 2);
        BitConverter.GetBytes((ushort)0x18).CopyTo(a, 0xA);
        BitConverter.GetBytes(id).CopyTo(a, 0xE);
        BitConverter.GetBytes(content.Length).CopyTo(a, 0x10);
        BitConverter.GetBytes((ushort)contentOffset).CopyTo(a, 0x14);
        nameBytes.CopyTo(a, 0x18);
        content.CopyTo(a, contentOffset);
        return a;
    }

    /// <summary>A non-resident attribute with the given (already encoded) data runs</summary>
    public static byte[] NonResident(int type, ulong size, byte[] runs, string name = null)
    {
        var nameBytes = name == null ? Array.Empty<byte>() : Encoding.Unicode.GetBytes(name);
        var runsOffset = Align(0x40 + nameBytes.Length);
        var a = new byte[Align(runsOffset + runs.Length + 1)];
        BitConverter.GetBytes(type).CopyTo(a, 0);
        BitConverter.GetBytes(a.Length).CopyTo(a, 4);
        a[8] = 1;
        a[9] = (byte)(nameBytes.Length / 2);
        BitConverter.GetBytes((ushort)0x40).CopyTo(a, 0xA);
        BitConverter.GetBytes((ulong)((size + 4095) / 4096 - 1)).CopyTo(a, 0x18); //last VCN
        BitConverter.GetBytes((ushort)runsOffset).CopyTo(a, 0x20);
        BitConverter.GetBytes((size + 4095) / 4096 * 4096).CopyTo(a, 0x28);
        BitConverter.GetBytes(size).CopyTo(a, 0x30);
        BitConverter.GetBytes(size).CopyTo(a, 0x38);
        nameBytes.CopyTo(a, 0x40);
        runs.CopyTo(a, runsOffset);
        return a;
    }

    public static byte[] StandardInfo(DateTime created)
    {
        var c = new byte[0x48];
        var t = FileTime(created);
        for (var i = 0; i < 4; i++)
        {
            BitConverter.GetBytes(t + i).CopyTo(c, i * 8);
        }

        BitConverter.GetBytes(0x20).CopyTo(c, 0x20); //archive
        BitConverter.GetBytes(0x100).CopyTo(c, 0x34); //security id
        return Resident(0x10, c);
    }

    public static byte[] FileNameContent(uint parent, ushort parentSeq, string name, byte nameType = 1,
        DateTime? created = null, ulong size = 0)
    {
        var n = Encoding.Unicode.GetBytes(name);
        var c = new byte[0x42 + n.Length];
        BitConverter.GetBytes((ulong)parent | (ulong)parentSeq << 48).CopyTo(c, 0);
        var t = FileTime(created ?? new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        for (var i = 0; i < 4; i++)
        {
            BitConverter.GetBytes(t).CopyTo(c, 8 + i * 8);
        }

        BitConverter.GetBytes((size + 4095) / 4096 * 4096).CopyTo(c, 0x28);
        BitConverter.GetBytes(size).CopyTo(c, 0x30);
        c[0x40] = (byte)name.Length;
        c[0x41] = nameType;
        n.CopyTo(c, 0x42);
        return c;
    }

    public static byte[] FileName(uint parent, ushort parentSeq, string name, byte nameType = 1) =>
        Resident(0x30, FileNameContent(parent, parentSeq, name, nameType));

    public static byte[] DataResident(byte[] data, string name = null) => Resident(0x80, data, name);

    public static byte[] Bytes(params int[] values) => values.Select(t => (byte)t).ToArray();

    public static int Align(int value) => (value + 7) & ~7;

    /// <summary>A file of records, as an $MFT</summary>
    public static byte[] Mft(params byte[][] records)
    {
        using var ms = new MemoryStream();
        foreach (var r in records)
        {
            ms.Write(r, 0, r.Length);
        }

        return ms.ToArray();
    }

    /// <summary>The system records an $MFT starts with, enough for paths: 0 ($MFT) and 5 (root)</summary>
    public static byte[] SystemRecord(uint entry, string name, bool directory = false) =>
        Record(entry, (ushort)(directory ? 3 : 1), new[] { StandardInfo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), FileName(5, 5, name) },
            sequence: (ushort)(entry == 5 ? 5 : 1));
}
