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

            using (var fs = new FileStream(bootFilePath, FileMode.Open, FileAccess.Read))
            {
                return new Boot(fs);
            }
        }

       
    }
}