using System.IO;

namespace Secure
{
    public static class SdhFile
    {
        public static Sdh Load(string sdhFile)
        {
            if (File.Exists(sdhFile) == false)
            {
                throw new FileNotFoundException($"'{sdhFile}' not found");
            }

            using (var br = new BinaryReader(new FileStream(sdhFile, FileMode.Open, FileAccess.Read)))
            {
                var bytes = ReadAllBytes(br);
                
                return new Sdh(bytes);
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