using System.IO;
using Secure;

namespace SDS
{
    public static class SdsFile
    {
        public static Sds Load(string sdsFile)
        {
            if (File.Exists(sdsFile) == false)
            {
                throw new FileNotFoundException($"'{sdsFile}' not found");
            }

            using (var br = new BinaryReader(new FileStream(sdsFile, FileMode.Open, FileAccess.Read)))
            {
                var bytes = ReadAllBytes(br);
                
                return new Sds(bytes);
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