namespace MFT.Other;

/// <summary>
///     One name in a directory: a $FILE_NAME (not a DOS name) whose parent is the directory
/// </summary>
public class ParentMapEntry
{
    public ParentMapEntry(string fileName, string key, bool isDirectory)
    {
        FileName = fileName;
        Key = key;
        IsDirectory = isDirectory;
    }

    public ParentMapEntry(string fileName, string key, bool isDirectory, FileRecord record) : this(fileName, key, isDirectory)
    {
        Record = record;
    }

    public string FileName { get; }

    /// <summary>
    ///     The file's entry and sequence numbers in decimal ("entry-sequence"; a deleted record's sequence number - 1).
    ///     <see cref="Record" /> is the record itself, without parsing this
    /// </summary>
    public string Key { get; }

    public bool IsDirectory { get; }

    /// <summary>
    ///     The FILE record the name is in. Set in the entries of <see cref="Mft" />'s directory maps
    ///     (<see cref="Mft.GetDirectoryContents" />, <see cref="Mft.GetDirectoryEntries" />); null when made with the
    ///     constructor without it
    /// </summary>
    public FileRecord Record { get; }

    public override string ToString()
    {
        return $"{FileName} IsDir: {IsDirectory} Key: {Key}";
    }
}
