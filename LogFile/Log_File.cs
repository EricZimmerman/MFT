using System.IO;

namespace LogFile;

public static class Log_File
{
    public static LogFile Load(string logFile)
    {
        if (File.Exists(logFile) == false) throw new FileNotFoundException($"'{logFile}' not found");

        using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read))
        {
            return new LogFile(fs);
        }
    }

    public static byte[] ReadAllBytes(this BinaryReader reader)
    {
        const int bufferSize = 4096;
        using (var ms = new MemoryStream())
        {
            var buffer = new byte[bufferSize];
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                ms.Write(buffer, 0, count);
            return ms.ToArray();
        }
    }
}