using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace Usn;

public class UsnFile
{
    public static Usn Load(string usnFilePath)
    {
        if (File.Exists(usnFilePath) == false) throw new FileNotFoundException($"'{usnFilePath}' not found");

        long start = 0;

        using (var br = new BinaryReader(new FileStream(usnFilePath, FileMode.Open, FileAccess.Read)))
        {
            Log.Verbose("Binary reader open");

            var firstByte = br.ReadByte();
            br.BaseStream.Seek(0, SeekOrigin.Begin);

            if (firstByte != 0)
            {
                Log.Verbose("First byte is not zero. Reading everything");

                return new Usn(br.BaseStream, 0);
            }

            Log.Verbose("Beginning data appears to be sparse");
            //beginning is sparse, so we have to find the start of the data

            start = FindStartingOffset(br.BaseStream);
        }

        return new Usn(new FileStream(usnFilePath, FileMode.Open, FileAccess.Read), start);
    }

    private static bool CheckByteRangeAllZeros(byte[] buff)
    {
        return buff.All(b => b == 0);
    }

    public static long FindStartingOffset(Stream usnBytes)
    {
        long startIndex = 0;

        using (var br = new BinaryReader(usnBytes, Encoding.ASCII, true))
        {
            Log.Verbose("Binary reader open");

            var firstByte = br.ReadByte();
            br.BaseStream.Seek(0, SeekOrigin.Begin);

            if (firstByte != 0) return 0;

            Log.Verbose("Beginning data appears to be sparse");
            //beginning is sparse, so we have to find the start of the data

            long startOffset;
            long lastCheckedOffset = 0;

            long lastDataOffset = 0;

            var currentCheckOffset = br.BaseStream.Length / 2;

            while (true)
            {
                Log.Verbose(
                    "currentCheckOffset: {CurrentCheckOffset:X}, lastCheckedOffset: 0x{LastCheckedOffset:X}, lastDataOffset: 0x{LastDataOffset:X}",
                    currentCheckOffset, lastCheckedOffset, lastDataOffset);

                if (lastDataOffset > 0 && lastDataOffset - lastCheckedOffset < 300)
                {
                    //we are close enough and will walk it out from here.
                    startOffset = lastCheckedOffset;
                    break;
                }

                br.BaseStream.Seek(currentCheckOffset, SeekOrigin.Begin);
                //0x90 bytes of 0s is good enough to know where to start
                var bcheck = br.ReadBytes(0x90);

                if (CheckByteRangeAllZeros(bcheck) == false)
                {
                    //we are in data
                    lastDataOffset = currentCheckOffset;

                    //we know we didn't have any data at lastCheckedOffset the last time we looked, so go backwards half way between there and lastDataOffset
                    currentCheckOffset = lastCheckedOffset + (currentCheckOffset - lastCheckedOffset) / 2;
                }
                else
                {
                    //no data, so move forward
                    lastCheckedOffset = currentCheckOffset;

                    currentCheckOffset = currentCheckOffset + (br.BaseStream.Length - currentCheckOffset) / 2;
                }
            }

            br.BaseStream.Seek(startOffset, SeekOrigin.Begin);

            //ignore zeros until we get to the first size
            while (br.PeekChar() == 0)
            {
                br.ReadByte();
                startOffset += 1;
            }


            startIndex = br.BaseStream.Position;
        }

        usnBytes.Seek(0, SeekOrigin.Begin);
        return startIndex;
    }
}