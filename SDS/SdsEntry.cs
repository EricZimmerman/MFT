using System;
using MFT.Attributes;

namespace Secure
{
    public class SdsEntry
    {
        private readonly uint _hash;

        public SdsEntry(uint hash, int id, long offset, int size, SKSecurityDescriptor sk, long fileOffset)
        {
            _hash = hash;
            Id = id;
            Offset = offset;
            Size = size;
            SecurityDescriptor = sk;
            FileOffset = fileOffset;
        }


        public string Hash => GetHash();

        public int Id { get; }
        public long Offset { get; }
        public int Size { get; }
        public long FileOffset { get; }

        public SKSecurityDescriptor SecurityDescriptor { get; }

        private string GetHash()
        {
            var b = BitConverter.GetBytes(_hash);
            return $"{b[0]:X2}{b[1]:X2}{b[2]:X2}{b[3]:X2}";
        }
    }
}