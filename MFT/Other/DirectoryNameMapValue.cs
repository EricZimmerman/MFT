namespace MFT.Other;

internal class DirectoryNameMapValue
{
    public DirectoryNameMapValue(string name, string parentRecordKey, bool isDeleted)
    {
        Name = name;
        IsDeleted = isDeleted;
        ParentRecordKey = parentRecordKey;
    }

    public string Name { get; }
    public string ParentRecordKey { get; }
    public bool IsDeleted { get; }

    public override string ToString()
    {
        return $"{Name}, Parent key: {ParentRecordKey} Deleted: {IsDeleted}";
    }
}