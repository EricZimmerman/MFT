using System;
using System.Collections.Generic;
using System.IO;
using MFT;
using MFT.Other;
using Serilog;

namespace I30;

public class I30
{
    /// <summary>INDX pages (index records) are 4096 bytes each</summary>
    private const int PageSize = 0x1000;

    /// <summary>The index node header (entries offset, sizes, flags) starts at 0x18; its offsets are relative to it</summary>
    private const int NodeHeaderOffset = 0x18;

    public I30(Stream fileStream)
    {
        var sig = 0x58444E49;

        Entries = new List<IndexEntryI30>();

        var pages = new List<byte[]>();

        using (var br = new BinaryReader(fileStream))
        {
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                pages.Add(br.ReadBytes(PageSize));
            }
        }

        var uniqueSlackEntryMd5s = new HashSet<string>();

        var pageNumber = 0;

        foreach (var page in pages)
        {
            //INDX pages are 4096 bytes each, so process them accordingly
            Log.Debug("Processing page 0x{PageNumber:X}", pageNumber);

            var pageOffset = (long)pageNumber * PageSize;

            if (page.Length < 0x28)
            {
                ParseErrors.Add($"Page at 0x{pageOffset:X} is only 0x{page.Length:X} bytes");
                break;
            }

            var sigActual = BitConverter.ToInt32(page, 0);

            if (sigActual == 0x00)
            {
                //empty page
                Log.Warning("Empty page found at offset {Offset}. Skipping", $"0x{pageOffset:X}");
                pageNumber++;
                continue;
            }

            if (sig != sigActual)
            {
                if (pageNumber == 0)
                {
                    throw new Exception("Invalid header! Expected 'INDX' Signature");
                }

                Log.Warning("Page at offset {Offset} has no 'INDX' signature. Skipping", $"0x{pageOffset:X}");
                ParseErrors.Add($"Page at 0x{pageOffset:X} has no 'INDX' signature; skipped");
                pageNumber++;
                continue;
            }

            try
            {
                ProcessPage(page, pageNumber, uniqueSlackEntryMd5s);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Error processing page at offset {Offset}: {Message}. Skipping", $"0x{pageOffset:X}",
                    e.Message);
                ParseErrors.Add($"Page at 0x{pageOffset:X} could not be processed: {e.Message}");
            }

            pageNumber += 1;
        }
    }

    private void ProcessPage(byte[] page, int pageNumber, HashSet<string> uniqueSlackEntryMd5s)
    {
        var pageOffset = (long)pageNumber * PageSize;

        var fixupOffset = BitConverter.ToInt16(page, 4);
        var numFixupPairs = BitConverter.ToInt16(page, 6);
        var dataStartOffset = BitConverter.ToInt32(page, NodeHeaderOffset);
        var dataSize = BitConverter.ToInt32(page, NodeHeaderOffset + 4);

        //fixups: the last 2 bytes of each 512 byte sector were replaced with the update sequence number;
        //put the original bytes back
        if (fixupOffset > 0 && numFixupPairs > 0 && fixupOffset + numFixupPairs * 2 <= page.Length)
        {
            var fixupBuffer = new byte[numFixupPairs * 2];
            Buffer.BlockCopy(page, fixupOffset, fixupBuffer, 0, fixupBuffer.Length);

            var fixupData = new FixupData(fixupBuffer);

            var sectorEnd = 512 - 2;
            foreach (var bytese in fixupData.FixupActual)
            {
                if (sectorEnd + 2 > page.Length)
                {
                    break;
                }

                var expected = BitConverter.ToInt16(page, sectorEnd);
                if (expected != fixupData.FixupExpected)
                {
                    Log.Warning(
                        "Fixup values do not match at 0x{FixupOffset1:X}. Expected: 0x{FixupExpected:X2}, actual: 0x{Expected:X2}",
                        sectorEnd, fixupData.FixupExpected, expected);
                }

                //replace fixup expected with actual bytes. bytese has actual replacement values in it.
                Buffer.BlockCopy(bytese, 0, page, sectorEnd, 2);

                sectorEnd += 512;
            }
        }
        else
        {
            ParseErrors.Add(
                $"Page at 0x{pageOffset:X}: fixup array at 0x{fixupOffset:X} with 0x{numFixupPairs:X} entries does not fit; fixups not applied");
        }

        //entries are from the data start to the data size (both relative to 0x18); after that is slack
        var activeStart = NodeHeaderOffset + dataStartOffset;
        var activeEnd = NodeHeaderOffset + dataSize;

        if (dataStartOffset < 0x10 || activeStart > page.Length || activeEnd < activeStart || activeEnd > page.Length)
        {
            ParseErrors.Add(
                $"Page at 0x{pageOffset:X}: entries from 0x{dataStartOffset:X} to 0x{dataSize:X} do not fit in the page");
            return;
        }

        var index = activeStart;

        while (activeEnd - index >= 0x10)
        {
            var absoluteOffset = pageOffset + index;

            Log.Verbose(
                "IN ACTIVE LOOP: Absolute offset: 0x{AbsoluteOffset:X} brActive.BaseStream.Position: 0x{Position:X}",
                absoluteOffset, index - activeStart);

            //MFT reference (8), entry size (2), key size (2), flags (4), key
            var indexSize = BitConverter.ToInt16(page, index + 8);

            if (indexSize < 0x10 || indexSize > activeEnd - index)
            {
                //a size of 0 used to loop forever
                ParseErrors.Add(
                    $"Page at 0x{pageOffset:X}: index entry at 0x{absoluteOffset:X} has size 0x{indexSize:X}; rest of the page skipped");
                break;
            }

            var indxBuffer = new byte[indexSize];
            Buffer.BlockCopy(page, index, indxBuffer, 0, indexSize);

            index += indexSize;

            IndexEntryI30 ie;
            try
            {
                ie = new IndexEntryI30(indxBuffer, absoluteOffset, pageNumber, false);
            }
            catch (Exception e)
            {
                ParseErrors.Add(
                    $"Page at 0x{pageOffset:X}: index entry at 0x{absoluteOffset:X} could not be parsed: {e.Message}");
                continue;
            }

            if (ie.MftReferenceSelf.MftEntryNumber == 0)
            {
                continue;
            }

            //its ok
            Log.Debug("{Ie}", ie);
            Entries.Add(ie);
        }

        Log.Verbose("IN SLACK LOOP for {Page}", pageNumber);

        var slackSpace = new byte[page.Length - activeEnd];
        Buffer.BlockCopy(page, activeEnd, slackSpace, 0, slackSpace.Length);

        var slackAbsOffset = pageOffset + activeEnd;

        var slackIe = FileRecord.GetSlackFileEntries(slackSpace, pageNumber, (int)slackAbsOffset, 0);

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

    public List<IndexEntryI30> Entries { get; }

    /// <summary>
    ///     Problems found: pages without a signature or that don't fit, entries with bad sizes. The entries that could be
    ///     read are kept.
    /// </summary>
    public List<string> ParseErrors { get; } = new();
}
