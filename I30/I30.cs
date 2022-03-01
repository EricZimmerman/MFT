using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MFT.Other;
using Serilog;
using FileInfo = MFT.Attributes.FileInfo;

namespace I30;

public class I30
{
    public I30(Stream fileStream)
    {
        var pageSize = 0x1000;

        //  var rawBytes2 = new byte[fileStream.Length];
        //  fileStream.Read(rawBytes2, 0, (int) fileStream.Length);

        var sig = 0x58444E49;

        Entries = new List<IndexEntry>();

        var pages = new List<byte[]>();

        using (var br = new BinaryReader(fileStream))
        {
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                pages.Add(br.ReadBytes(pageSize));
            }
        }

        var uniqueSlackEntryMd5s = new HashSet<string>();

        var pageNumber = 0;
        foreach (var page in pages)
        {
            //INDX pages are 4096 bytes each, so process them accordingly

            Log.Debug("Processing page 0x{PageNumber:X}", pageNumber);

            using (var br = new BinaryReader(new MemoryStream(page)))
            {
                var sigActual = br.ReadInt32();

                if (sig != sigActual)
                {
                    throw new Exception("Invalid header! Expected 'INDX' Signature.");
                }

                var fixupOffset = br.ReadInt16();

                var numFixupPairs = br.ReadInt16();

                var logFileSequenceNumber = br.ReadInt64();

                var virtualClusterNumber = br.ReadInt64();

                var dataStartOffset = br.ReadInt32();
                var dataSize = br.ReadInt32();
                var dataSizeAllocated = br.ReadInt32();

                var isLeafNode = br.ReadInt32() == 0; //this gets us by padding too

                var fixupTotalLength = numFixupPairs * 2;

                var fixupBuffer = new byte[fixupTotalLength];

                fixupBuffer = br.ReadBytes(fixupTotalLength);

                while (br.BaseStream.Position % 8 != 0)
                {
                    br.ReadByte(); //gets us past padding
                }

                //since we need to change bytes for the index entries based on fixup, get an array of those bytes

                var rawBytes = br.ReadBytes((int)(br.BaseStream.Length - br.BaseStream.Position));

                var fixupData = new FixupData(fixupBuffer);

                //fixup verification
                var counter =
                    512 - dataStartOffset -
                    0x18; //datastartOffset is relative, so we need to account for where it begins, at 0x18
                foreach (var bytese in fixupData.FixupActual)
                {
                    //adjust the offset to where we need to check
                    var fixupOffset1 = counter - 2;

                    var expected = BitConverter.ToInt16(rawBytes, fixupOffset1);
                    if (expected != fixupData.FixupExpected)
                    {
                        Log.Warning(
                            "Fixup values do not match at 0x{FixupOffset1:X}. Expected: 0x{FixupExpected:X2}, actual: 0x{Expected:X2}",
                            fixupOffset1, fixupData.FixupExpected, expected);
                    }

                    //replace fixup expected with actual bytes. bytese has actual replacement values in it.
                    Buffer.BlockCopy(bytese, 0, rawBytes, fixupOffset1, 2);

                    counter += 512;
                }

                //rawbytes contains the data from the current page we need to parse to get to indexes
                //datasize includes startoffset plus fixup, etc, so subtract data offset from size for the active index allocations
                //valid data is allocated - dataoffset
                //after that is slack

                var activeSpace = new byte[dataSize - dataStartOffset];
                Buffer.BlockCopy(rawBytes, 0, activeSpace, 0, activeSpace.Length);

                var slackSpace = new byte[rawBytes.Length - activeSpace.Length];
                Buffer.BlockCopy(rawBytes, dataSize - dataStartOffset, slackSpace, 0, slackSpace.Length);

                // File.WriteAllBytes($@"C:\temp\{pageNumber}_slack.bin",slackSpace);

                //absolute offset is page # * 0x1000 + 0x18 + datastartoffset
                //for slack, add activespace.len

                using (var binaryReader = new BinaryReader(new MemoryStream(activeSpace)))
                {
                    while (binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
                    {
                        var absoluteOffset = pageNumber * 0x1000 + 0x18 + dataStartOffset +
                                             binaryReader.BaseStream.Position;

                        Log.Verbose(
                            "IN ACTIVE LOOP: Absolute offset: 0x{AbsoluteOffset:X} brActive.BaseStream.Position: 0x{Position:X}",
                            absoluteOffset, binaryReader.BaseStream.Position);

                        binaryReader.ReadInt64(); //mft info
                        var indexSize = binaryReader.ReadInt16();
                        binaryReader.BaseStream.Seek(-10, SeekOrigin.Current); //go back to start of the index data

                        var indxBuffer = binaryReader.ReadBytes(indexSize);

                        var ie = new IndexEntry(indxBuffer, absoluteOffset, pageNumber, false);

                        if (ie.Flag == IndexEntry.OEntryFlag.LastEntry)
                        {
                            Debug.Write(1);
                        }

                        if (ie.MftReferenceSelf.MftEntryNumber != 0)
                        {
                            //its ok
                            Log.Debug("{Ie}", ie);
                            Entries.Add(ie);
                        }
                    }
                }

                var h = GetUnicodeHits(slackSpace);

                var slackAbsOffset = pageNumber * 0x1000 + 0x18 + dataStartOffset +
                                     activeSpace.Length;

                Log.Verbose("IN SLACK LOOP for {Page", pageNumber);

                foreach (var hitInfo in h)
                {
                    Log.Verbose("Processing offset {O} {H}", hitInfo.Offset, hitInfo.Hit);


                    //contains offset to start of hit and hit, but we only need start of the string to know where to begin
                    //the start of the record is 0x42 bytes from where the hit is
                    //since we know the offset of the hit, subtract 2 to get length of decoded string.
                    //multiply by 2 for # of bytes we need to read.
                    //add this to get the total length of the data we need to read adn read into slackspace as needed

                    var nameSize = slackSpace[hitInfo.Offset - 2];
                    var start = hitInfo.Offset - 0x42;
                    var end = hitInfo.Offset + nameSize * 2;

                    var buffSize = end - start;

                    var buff = new byte[buffSize];
                    Buffer.BlockCopy(slackSpace, start, buff, 0, buffSize);

                    var md5 = GetMd5(buff);

                    if (uniqueSlackEntryMd5s.Contains(md5))
                    {
                        Log.Debug("Discarding duplicate slack buffer with MD5 {Md5}", md5);
                        continue;
                    }

                    var slackIndex = new IndexEntry(buff, slackAbsOffset + start - 0x10, pageNumber, true);

                    //some cleanup of questionable stuff
                    if (slackIndex.FileInfo.NameLength == 0)
                    {
                        continue;
                    }


                    Log.Debug("{Ie}", slackIndex);
                    Entries.Add(slackIndex);

                    uniqueSlackEntryMd5s.Add(md5);
                }
            }

            pageNumber += 1;
        }
    }


    public List<IndexEntry> Entries { get; }


    private string GetMd5(byte[] input)
    {
        using var myHash = MD5.Create();
        var byteArrayResult =
            myHash.ComputeHash(input);
        return
            string.Concat(Array.ConvertAll(byteArrayResult,
                h => h.ToString("X2")));
    }


    private List<HitInfo> GetUnicodeHits(byte[] bytes)
    {
        var maxString = "";
        var mi2 = $"{"{"}{3}{","}{maxString}{"}"}";

        var uniRange = "[\u0020-\u007E]";
        var regUni = new Regex($"{uniRange}{mi2}", RegexOptions.Compiled);
        var uniString = Encoding.Unicode.GetString(bytes);

        var hits = new List<HitInfo>();

        foreach (Match match in regUni.Matches(uniString))
        {
            if (match.Value.Trim().Length == 0)
            {
                continue;
            }

            var actualOffset = match.Index * 2;

            var hi = new HitInfo(actualOffset, match.Value.Trim());
            hits.Add(hi);
        }

        return hits;
    }
}

public class HitInfo
{
    public HitInfo(int offset, string hit)
    {
        Offset = offset;
        Hit = hit;
    }

    public int Offset { get; set; }
    public string Hit { get; set; }

    public override string ToString()
    {
        return $"0x{Offset:X}: {Hit}";
    }
}

public class IndexEntry
{
    public enum OEntryFlag
    {
        HasSubNodes = 0x1,
        LastEntry = 0x2
    }

    public IndexEntry(byte[] rawBytes, long absoluteOffset, int pageNumber, bool fromSlack)
    {
        PageNumber = pageNumber;
        FromSlack = fromSlack;

        AbsoluteOffset = absoluteOffset;

        using var br = new BinaryReader(new MemoryStream(rawBytes));

        var skipOffset = 0;
        if (fromSlack == false)
        {
            MftReferenceSelf = new MftEntryInfo(br.ReadBytes(8));

            if (MftReferenceSelf.MftEntryNumber == 0)
            {
                return;
            }

            var indexEntrySize = br.ReadInt16();
            var indexDataSize = br.ReadInt16();
            Flag = (OEntryFlag)br.ReadInt32();
            skipOffset = 8 + 2 + 2 + 4;
        }

        FileInfo = new FileInfo(rawBytes.Skip(skipOffset).ToArray());
    }

    public OEntryFlag Flag { get; }

    public int PageNumber { get; }
    public bool FromSlack { get; }

    public long AbsoluteOffset { get; }

    public MftEntryInfo MftReferenceSelf { get; }

    public FileInfo FileInfo { get; }


    public override string ToString()
    {
        return
            $"Absolute offset: 0x{AbsoluteOffset:X} FromSlack: {FromSlack} Self MFT: {MftReferenceSelf} FileInfo: {FileInfo}";
    }
}