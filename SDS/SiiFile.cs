using System.IO;

namespace Secure
{
    public static class SiiFile
    {
        public static Sii Load(string siiFile)
        {
            if (File.Exists(siiFile) == false)
            {
                throw new FileNotFoundException($"'{siiFile}' not found");
            }

            using (var br = new BinaryReader(new FileStream(siiFile, FileMode.Open, FileAccess.Read)))
            {
                var bytes = ReadAllBytes(br);
                
                return new Sii(bytes);
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