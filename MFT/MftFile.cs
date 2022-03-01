using System.IO;

namespace MFT;

public static class MftFile
{
    public static string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

    public static Mft Load(string mftPath)
    {
        if (File.Exists(mftPath) == false)
        {
            throw new FileNotFoundException($"'{mftPath}' not found");
        }

        using var fs = new FileStream(mftPath, FileMode.Open, FileAccess.Read);
        return new Mft(fs);
    }
}