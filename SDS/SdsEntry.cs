using System;
using MFT.Attributes;

namespace Secure;

public class SdsEntry
{
    private readonly uint _hash;

    public SdsEntry(uint hash, uint id, ulong offset, uint size, SkSecurityDescriptor sk, ulong fileOffset)
    {
        _hash = hash;
        Id = id;
        Offset = offset;
        Size = size;
        SecurityDescriptor = sk;
        FileOffset = fileOffset;
    }


    public string Hash => GetHash();

    public uint Id { get; }
    public ulong Offset { get; }
    public uint Size { get; }
    public ulong FileOffset { get; }

    public SkSecurityDescriptor SecurityDescriptor { get; }

    /// <summary>True for a copy from the mirror block that differs from the primary copy</summary>
    public bool FromMirror { get; internal set; }

    private string GetHash()
    {
        var b = BitConverter.GetBytes(_hash);
        return $"{b[0]:X2}{b[1]:X2}{b[2]:X2}{b[3]:X2}";
    }
}