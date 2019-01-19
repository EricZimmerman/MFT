using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFT.Other
{
    public class ParentMapEntry
    {
        public ParentMapEntry(string fileName, string key, bool isDirectory)
        {
            FileName = fileName;
            Key = key;
            IsDirectory = isDirectory;
        }

        public string FileName { get; }
        public string Key { get; }
        public bool IsDirectory { get; }

        public override string ToString()
        {
            return $"{FileName} IsDir: {IsDirectory} Key: {Key}";
        }
    }
}
