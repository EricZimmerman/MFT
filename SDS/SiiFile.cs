using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Secure
{
    public static class SiiFile
    { public static Sii Load(string ssiFile)
        {
            if (File.Exists(ssiFile) == false)
            {
                throw new FileNotFoundException($"'{ssiFile}' not found");
            }

            var bytes = File.ReadAllBytes(ssiFile);
         
            return new Sii(bytes);
        }

    }
}
