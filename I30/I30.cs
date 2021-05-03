using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MFT.Other;
using NLog;
using NLog.LayoutRenderers;
using Exception = System.Exception;
using FileInfo = MFT.Attributes.FileInfo;

namespace I30
{
    public class I30
    {
        public I30(Stream fileStream)
        {
            var logger = LogManager.GetLogger("I30");

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

            var pageNumber = 0;
            foreach (var page in pages)
            {
                //INDX pages are 4096 bytes each, so process them accordingly

                logger.Debug($"Procesing page 0x{pageNumber:X}");

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
               
                    var rawBytes = br.ReadBytes((int) (br.BaseStream.Length - br.BaseStream.Position));

                    var fixupData = new FixupData(fixupBuffer);

                    //fixup verification
                    var counter = 512 - dataStartOffset - 0x18; //datastartOffset is relative, so we need to account for where it begins, at 0x18
                    foreach (var bytese in fixupData.FixupActual)
                    {
                        //adjust the offset to where we need to check
                        var fixupOffset1 = counter - 2;

                        var expected = BitConverter.ToInt16(rawBytes, fixupOffset1);
                        if (expected != fixupData.FixupExpected)
                        {
                            logger.Warn(
                                $"Fixup values do not match at 0x{fixupOffset1:X}. Expected: 0x{fixupData.FixupExpected:X2}, actual: 0x{expected:X2}");
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
                    Buffer.BlockCopy(rawBytes,0,activeSpace,0,activeSpace.Length);

                    var slackSpace = new byte[rawBytes.Length -  activeSpace.Length];
                    Buffer.BlockCopy(rawBytes,dataSize - dataStartOffset,slackSpace,0,slackSpace.Length);

                    //absolute offset is page # * 0x1000 + 0x18 + datastartoffset
                    //for slack, add activespace.len

                    using (var binaryReader = new BinaryReader(new MemoryStream(activeSpace)))
                    {
                        while (binaryReader.BaseStream.Position<binaryReader.BaseStream.Length)
                        {
                             var absoluteOffset = pageNumber * 0x1000 + 0x18 + dataStartOffset + binaryReader.BaseStream.Position;

                             logger.Trace($"IN ACTIVE LOOP: Absolute offset: 0x{absoluteOffset:X} brActive.BaseStream.Position: 0x{binaryReader.BaseStream.Position:X}");

                            binaryReader.ReadInt64();//mft info
                            var indexSize = binaryReader.ReadInt16();
                            binaryReader.BaseStream.Seek(-10, SeekOrigin.Current); //go back to start of the index data

                            var indxBuffer = binaryReader.ReadBytes(indexSize);

                            var ie = new IndexEntry(indxBuffer,absoluteOffset,pageNumber,false);

                            if (ie.MftReferenceSelf.MftEntryNumber != 0)
                            {
                                //its ok
                                logger.Info(ie);
                                Entries.Add(ie);
                            }
                        }
                    }

                    
                    using (var binaryReader = new BinaryReader(new MemoryStream(slackSpace)))
                    {

                        while (binaryReader.BaseStream.Position<binaryReader.BaseStream.Length)
                        {
                            var absoluteOffset = pageNumber * 0x1000 + 0x18 + dataStartOffset + binaryReader.BaseStream.Position + activeSpace.Length;

                            logger.Info($"IN SLACK LOOP: Absolute offset: 0x{absoluteOffset:X} brActive.BaseStream.Position: 0x{binaryReader.BaseStream.Position:X}");

                            //in slack
                            //read mft
                            //then dates start
                            //so have to read into the structure far enough to know where the filename ends, then pad it out?
                            

                            binaryReader.ReadInt64();//mft info
                            var indexSize = binaryReader.ReadInt16();
                            binaryReader.BaseStream.Seek(-10, SeekOrigin.Current); //go back to start of the index data

                            var indxBuffer = binaryReader.ReadBytes(indexSize);

                            var ie = new IndexEntry(indxBuffer,absoluteOffset,pageNumber,true);

                            if (ie.MftReferenceSelf.MftEntryNumber != 0)
                            {
                                //its ok
                                logger.Info(ie);
                                Entries.Add(ie);
                            }
                        }
                    }
                   

                }

                pageNumber += 1;
            }


        }

        public List<IndexEntry> Entries { get; }
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

            using (var br = new BinaryReader(new MemoryStream(rawBytes)))
            {
                MftReferenceSelf = new MftEntryInfo(br.ReadBytes(8));

                if (MftReferenceSelf.MftEntryNumber == 0)
                {
                    return;
                }

                var indexEntrySize = br.ReadInt16();
                var indexDataSize = br.ReadInt16();
                var flags = br.ReadInt32();

                FileInfo = new FileInfo(br.ReadBytes((int) (br.BaseStream.Length - br.BaseStream.Position)));
      
            }
        }

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
}