using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFT.Other
{
public    class Directory
    {
        public string Name { get; }

        /// <summary>
        /// The key into FileRecords collection
        /// <remarks>Format is '$"{f.EntryNumber}-{f.SequenceNumber}"'</remarks>
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Contains references to FileRecords that live in this Directory
        /// <remarks>Subitems need to be checked to determine whether they are directories or files via $STANDARD_INFO.Flags</remarks>
        /// </summary>
        public Dictionary<string,Directory> SubItems { get; }

        public Directory(string name, string key)
        {
            Name = name;
            Key = key;
            SubItems = new Dictionary<string, Directory>();
        }

    }
}
