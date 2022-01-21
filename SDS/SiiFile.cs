using System.IO;

namespace Secure;

public static class SiiFile
{
    public static Sii Load(string siiFile)
    {
        if (File.Exists(siiFile) == false) throw new FileNotFoundException($"'{siiFile}' not found");

        using (var fs = new FileStream(siiFile, FileMode.Open, FileAccess.Read))
        {
            return new Sii(fs);
        }
    }
}