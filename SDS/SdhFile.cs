using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Secure
{
    public static class SdhFile
    {
            public static Sdh Load(string sdsFile)
            {
                if (File.Exists(sdsFile) == false)
                {
                    throw new FileNotFoundException($"'{sdsFile}' not found");
                }

                var bytes = File.ReadAllBytes(sdsFile);

         
                return new Sdh(bytes);
            }
    }
}
