using System;
using System.Collections.Generic;
using System.IO;
using MFT;
using MFT.Other;
using Serilog;

namespace I30;

public class I30
{
    public I30(Stream fileStream)
    {
        var pageSize = 0x1000;

        var sig = 0x58444E49;

        Entries = new List<IndexEntryI30>();

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

                if (sigActual == 0x00)
                {
                    //empty page
                    Log.Warning("Empty page found at offset {Offset}. Skipping", $"0x{pageNumber * 0x1000:X}");
                    pageNumber++;
                    continue;
                }
                
                if (sig != sigActual)
                {
                    throw new Exception("Invalid header! Expected 'INDX' Signature");
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

                        var ie = new IndexEntryI30(indxBuffer, absoluteOffset, pageNumber, false);

                        if (ie.MftReferenceSelf.MftEntryNumber == 0)
                        {
                            continue;
                        }

                        //its ok
                        Log.Debug("{Ie}", ie);
                        Entries.Add(ie);
                    }
                }

                Log.Verbose("IN SLACK LOOP for {Page", pageNumber);
                var slackAbsOffset = pageNumber * 0x1000 + 0x18 + dataStartOffset +
                                     activeSpace.Length;

                var slackIe = FileRecord.GetSlackFileEntries(slackSpace, pageNumber, slackAbsOffset,0);

                //var h = GetUnicodeHits(slackSpace);

                foreach (var indexEntry in slackIe)
                {
                    if (uniqueSlackEntryMd5s.Contains(indexEntry.Md5))
                    {
                        Log.Debug("Discarding duplicate slack buffer with MD5 {Md5}", indexEntry.Md5);
                        continue;
                    }

                    Entries.Add(indexEntry);

                    uniqueSlackEntryMd5s.Add(indexEntry.Md5);

                }
            }

            pageNumber += 1;
        }
    }


    public List<IndexEntryI30> Entries { get; }



}

