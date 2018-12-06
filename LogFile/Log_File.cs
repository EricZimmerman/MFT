using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogFile
{
    public static class Log_File
    {
        public static LogFile Load(string logFile)
        {
            if (File.Exists(logFile) == false)
            {
                throw new FileNotFoundException($"'{logFile}' not found");
            }

            using (var br = new BinaryReader(new FileStream(logFile, FileMode.Open, FileAccess.Read)))
            {
                var bytes = ReadAllBytes(br);
                
                return new LogFile(bytes);
            }
        }

        public static byte[] ReadAllBytes(this BinaryReader reader)
        {
            const int bufferSize = 4096;
            using (var ms = new MemoryStream())
            {
                byte[] buffer = new byte[bufferSize];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                    ms.Write(buffer, 0, count);
                return ms.ToArray();
            }

        }
    }
}