using System;

namespace MFT.Attributes
{
    public class IndexNodeHeader
    {
        public enum IndexNodeFlag
        {
            HasIndexAllocation = 0x01
        }

        public IndexNodeHeader(byte[] rawBytes)
        {
            var index = 0;

            IndexValuesOffset = BitConverter.ToInt32(rawBytes, index);
            index += 4;
            IndexNodeSize = BitConverter.ToInt32(rawBytes, index);
            index += 4;
            AllocatedIndexNodeSize = BitConverter.ToInt32(rawBytes, index);
            index += 4;
            IndexNodeFlags = (IndexNodeFlag) BitConverter.ToInt32(rawBytes, index);
        }

        public int IndexValuesOffset { get; }
        public int IndexNodeSize { get; }
        public int AllocatedIndexNodeSize { get; }
        public IndexNodeFlag IndexNodeFlags { get; }


        public override string ToString()
        {
            return
                $"IndexValuesOffset: 0x{IndexValuesOffset:X} IndexNodeSize: {IndexNodeSize} AllocatedIndexNodeSize: {AllocatedIndexNodeSize} IndexNodeFlags: {IndexNodeFlags}";
        }
    }
}