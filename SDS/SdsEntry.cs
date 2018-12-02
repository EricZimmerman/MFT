using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Secure
{
    public class SdsEntry
    {

        private uint _hash;
        public SdsEntry(uint hash, int id, long offset, int size, SecurityDescriptor sk)
        {
            _hash = hash;
            Id = id;
            Offset = offset;
            Size = size;
            SecurityDescriptor = sk;
        }


        public string Hash => GetHash();

        private string GetHash()
        {
                var b = BitConverter.GetBytes(_hash);
                return $"{b[0]:X2}{b[1]:X2}{b[2]:X2}{b[3]:X2}";
           
        }

        public int Id { get; }
        public long Offset { get; }
        public int Size { get; }

        public SecurityDescriptor SecurityDescriptor { get; }
    }
}
