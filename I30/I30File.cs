using System.IO;

namespace I30;

public static class I30File
{
    public static I30 Load(string indexFile)
    {
        if (File.Exists(indexFile) == false)
        {
            throw new FileNotFoundException($"'{indexFile}' not found");
        }

        using var fs = new FileStream(indexFile, FileMode.Open, FileAccess.Read);
        return new I30(fs);
    }
}