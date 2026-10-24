using System;
using System.Text;
using MFT.Other;

namespace MFT.Attributes;

public class FileName : Attribute
{
    public FileName(byte[] rawBytes) : this(rawBytes, default)
    {
    }

    internal FileName(byte[] rawBytes, in TimestampSource source) : base(rawBytes)
    {
        var content = new byte[rawBytes.Length - ContentOffset];

        Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, rawBytes.Length - ContentOffset);

        FileInfo = new FileInfo(content, source.WithAttribute("$FILE_NAME"));
    }

    public FileInfo FileInfo { get; }


    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine("**** FILE NAME ****");

        sb.AppendLine(base.ToString());

        //sb.AppendLine();

        sb.AppendLine(FileInfo.ToString());

        return sb.ToString();
    }
}