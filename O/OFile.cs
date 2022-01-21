using System.IO;

namespace O;

public static class OFile
{
    public static O Load(string siiFile)
    {
        if (File.Exists(siiFile) == false) throw new FileNotFoundException($"'{siiFile}' not found");

        using (var fs = new FileStream(siiFile, FileMode.Open, FileAccess.Read))
        {
            return new O(fs);
        }
    }
}