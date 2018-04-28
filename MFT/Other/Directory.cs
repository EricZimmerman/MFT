using System.Collections.Generic;
using System.Text;

namespace MFT.Other
{
    public class DirectoryItem
    {
        public DirectoryItem(string name, string key, string parentPath)
        {
            Name = name;
            Key = key;
            ParentPath = parentPath;
            SubItems = new Dictionary<string, DirectoryItem>();
        }

        public string Name { get; }
        public string ParentPath { get; }

        /// <summary>
        ///     The key into FileRecords collection
        ///     <remarks>Format is '$"{f.EntryNumber}-{f.SequenceNumber}"'</remarks>
        /// </summary>
        public string Key { get; }

        /// <summary>
        ///     Contains references to FileRecords that live in this Directory
        ///     <remarks>Subitems need to be checked to determine whether they are directories or files via $STANDARD_INFO.Flags</remarks>
        /// </summary>
        public Dictionary<string, DirectoryItem> SubItems { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Name: {Name}");
            foreach (var directory in SubItems)
            {
                sb.AppendLine(directory.Value.Name);
            }

            return sb.ToString();
        }
    }
}