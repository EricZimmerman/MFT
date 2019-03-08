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

            using (var fs = new FileStream(sdhFile, FileMode.Open, FileAccess.Read))
            {
                return new Sdh(fs);
            }
        }

     
    }
}