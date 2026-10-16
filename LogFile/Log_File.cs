using System.IO;

namespace LogFile;

public static class Log_File
{
    /// <param name="logFile">Path of the $LogFile</param>
    /// <param name="clusterSize">Bytes per cluster of the volume (from $Boot), to work out target $MFT entries</param>
    /// <exception cref="LogFileParseException">No valid restart page, or the log was never used</exception>
    public static LogFile Load(string logFile, int clusterSize = 4096)
    {
        if (File.Exists(logFile) == false)
        {
            throw new FileNotFoundException($"'{logFile}' not found");
        }

        using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read);
        return new LogFile(fs, clusterSize);
    }
}
