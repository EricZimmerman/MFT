using System;
using System.Text;

namespace MFT.Attributes;

public class ResidentData
{
    public ResidentData(byte[] rawBytes)
    {
        Data = rawBytes;
    }

    public byte[] Data { get; }

    public override string ToString()
    {
        var asAscii = Encoding.ASCII.GetString(Data);
        var asUnicode = Encoding.Unicode.GetString(Data);
        return $"Data: {BitConverter.ToString(Data)}\r\n\r\nASCII: {asAscii}\r\nUnicode: {asUnicode}";
    }
}