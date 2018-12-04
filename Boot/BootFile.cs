using System;
using System.IO;

namespace Boot
{
    public static class BootFile
    {
        public static Boot Load(string bootFilePath)
        {
            if (File.Exists(bootFilePath) == false)
            {
                throw new FileNotFoundException($"'{bootFilePath}' not found");
            }

            using (var br = new BinaryReader(new FileStream(bootFilePath, FileMode.Open, FileAccess.Read)))
            {
                var bytes = ReadAllBytes(br);

                return new Boot(bytes);
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