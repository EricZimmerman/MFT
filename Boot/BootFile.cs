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

            var bytes = File.ReadAllBytes(bootFilePath);

            var b = new byte[512];
            Buffer.BlockCopy(bytes, 0, b, 0, 512);

            return new Boot(b);
        }
    }
}