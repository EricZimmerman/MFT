using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace LogFile;

public class LogFile
{
    private const int PageSize = 0x1000;

    private const int RstrSig = 0x52545352;
    private const int RcrdSig = 0x44524352;
    private const int ChkdSig = 0x52545351;

    public static uint LastOffset;

    public LogFile(Stream fileStream)
    {
        //preliminary sig check to get us started
        const int sig = 0x52545352;

        var br = new BinaryReader(fileStream);

        var index = 0x0;
        var sigCheck = br.ReadInt32(); // BitConverter.ToInt32(rawBytes, index);

        if (sig != sigCheck)
        {
            throw new Exception("Invalid header! Expected 'RSTR' Signature.");
        }

        br.BaseStream.Seek(0, SeekOrigin.Begin); //reset

        NormalPageArea = new List<LogPageRcrd>();

        while (fileStream.Position < fileStream.Length)
        {
            LastOffset = (uint)index;

            var buff = br.ReadBytes(PageSize);

            Log.Debug("Processing log page at offset 0x{Index:X}", index);

            var sigActual = BitConverter.ToInt32(buff, 0);

            switch (sigActual)
            {
                case RstrSig:
                    var lprstr = new LogPageRstr(buff, index);

                    Log.Information("{Lprstr}", lprstr);

                    if (index == 0)
                    {
                        PrimaryRstrPage = lprstr;
                    }
                    else
                    {
                        SecondaryRstrPage = lprstr;
                    }

                    break;
                case RcrdSig:
//
                    LogPageRcrd lprcrd = null;

                    //loop thru all pages, then walk thru again, grouping into chunks based on PageCount
                    //then process each chunk with each page inside

                    try
                    {
                        lprcrd = new LogPageRcrd(buff, index);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }


                    if (index == 0x2000)
                    {
                        BufferPrimary = lprcrd;
                    }
                    else if (index == 0x3000)
                    {
                        BufferSecondary = lprcrd;
                    }
                    else
                    {
                        NormalPageArea.Add(lprcrd);
                    }


                    break;
//                case chkd_sig: //havent seen one of these to test
//                    PageType = PageTypes.Chkd;
//                    break;
                default:
                    throw new Exception(
                        $"Invalid signature at offset 0x{index:X}! Expected 'RCRD|RSTR|CHKD' signature.");
            }


            index += PageSize;
        }
    }

    public List<LogPageRcrd> NormalPageArea { get; }
    public LogPageRstr PrimaryRstrPage { get; }
    public LogPageRstr SecondaryRstrPage { get; }

    public LogPageRcrd BufferPrimary { get; }
    public LogPageRcrd BufferSecondary { get; }
}