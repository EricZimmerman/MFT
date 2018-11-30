using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SDS
{
  public static  class SdsFile
    {
        public static Sds Load(string sdsFile)
        {
            if (File.Exists(sdsFile) == false)
            {
                throw new FileNotFoundException($"'{sdsFile}' not found");
            }

            var bytes = File.ReadAllBytes(sdsFile);

         
            return new Sds(bytes);
        }
    }
}
