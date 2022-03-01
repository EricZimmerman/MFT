using System.IO;
using Secure;

namespace SDS;

public static class SdsFile
{
    public static Sds Load(string sdsFile)
    {
        if (File.Exists(sdsFile) == false)
        {
            throw new FileNotFoundException($"'{sdsFile}' not found");
        }

        using var fs = new FileStream(sdsFile, FileMode.Open, FileAccess.Read);
        return new Sds(fs);
    }
}