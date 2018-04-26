using System;

namespace MFT.Attributes
{
    public class ResidentData
    {
        public ResidentData(byte[] rawBytes)
        {
            Data = rawBytes;
        }

        public byte[] Data { get; }

        public override string ToString()
        {
            return $"Data: {BitConverter.ToString(Data)}";
        }
    }
}