using System.IO;

namespace MFT
{
    public static class MftFile
    {
        public static string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

        public static Mft Load(string mftPath)
        {
            if (File.Exists(mftPath) == false)
            {
                throw new FileNotFoundException($"'{mftPath}' not found");
            }

            using (var fs = new FileStream(mftPath, FileMode.Open, FileAccess.Read))
            {
                
                return new Mft(fs);
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