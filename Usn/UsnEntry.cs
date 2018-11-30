using System;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace Usn
{
    public class UsnEntry
    {
        [Flags]
        public enum FileAttributeFlag
        {
            ReadOnly = 0x01,
            Hidden = 0x02,
            System = 0x04,
            VolumeLabel = 0x08,
            Directory = 0x010,
            Archive = 0x020,
            Device = 0x040,
            Normal = 0x080,
            Temporary = 0x0100,
            SparseFile = 0x0200,
            ReparsePoint = 0x0400,
            Compressed = 0x0800,
            Offline = 0x01000,
            NotContentIndexed = 0x02000,
            Encrypted = 0x04000,
            IntegrityStream = 0x08000,
            Virtual = 0x010000,
            NoScrubData = 0x020000,
            HasEa = 0x040000,
            IsDirectory = 0x10000000,
            IsIndexView = 0x20000000
        }

        [Flags]
        public enum UpdateReasonFlag : uint
        {
            [Description(
                "A user has either changed one or more file or directory attributes (for example, the read-only, hidden, system, archive, or sparse attribute), or one or more time stamps.")]
            BasicInfoChange = 0x00008000,

            [Description("The file or directory is closed.")]
            Close = 0x80000000,

            [Description("The compression state of the file or directory is changed from or to compressed.")]
            CompressionChange = 0x00020000,

            [Description("The file or directory is extended (added to).")]
            DataExtend = 0x00000002,

            [Description("The data in the file or directory is overwritten.")]
            DataOverwrite = 0x00000001,

            [Description("The file or directory is truncated.")]
            DataTruncation = 0x00000004,

            [Description(
                "The user made a change to the extended attributes of a file or directory. These NTFS file system attributes are not accessible to Windows-based applications.")]
            EaChange = 0x00000400,

            [Description("The file or directory is encrypted or decrypted.")]
            EncryptionChange = 0x00040000,

            [Description("The file or directory is created for the first time.")]
            FileCreate = 0x00000100,

            [Description("The file or directory is deleted.")]
            FileDelete = 0x00000200,

            [Description(
                "An NTFS file system hard link is added to or removed from the file or directory. An NTFS file system hard link, similar to a POSIX hard link, is one of several directory entries that see the same file or directory.")]
            HardLinkChange = 0x00010000,

            [Description(
                "A user changes the FILE_ATTRIBUTE_NOT_CONTENT_INDEXED attribute. That is, the user changes the file or directory from one where content can be indexed to one where content cannot be indexed, or vice versa. Content indexing permits rapid searching of data by building a database of selected content.")]
            IndexableChange = 0x00004000,

            [Description(
                "A user changed the state of the FILE_ATTRIBUTE_INTEGRITY_STREAM attribute for the given stream. On the ReFS file system, integrity streams maintain a checksum of all data for that stream, so that the contents of the file can be validated during read or write operations.")]
            IntegrityChange = 0x00800000,

            [Description("The one or more named data streams for a file are extended (added to).")]
            NamedDataExtend = 0x00000020,

            [Description("The data in one or more named data streams for a file is overwritten.")]
            NamedDataOverwrite = 0x00000010,

            [Description("The one or more named data streams for a file is truncated.")]
            NamedDataTruncation = 0x00000040,

            [Description("The object identifier of a file or directory is changed.")]
            ObjectIdChange = 0x00080000,

            [Description(
                "A file or directory is renamed, and the file name in the USN_RECORD structure is the new name.")]
            RenameNewName = 0x00002000,

            [Description(
                "The file or directory is renamed, and the file name in the USN_RECORD structure is the previous name.")]
            RenameOldName = 0x00001000,

            [Description(
                "The reparse point that is contained in a file or directory is changed, or a reparse point is added to or deleted from a file or directory.")]
            ReparsePointChange = 0x00100000,

            [Description("A change is made in the access rights to a file or directory.")]
            SecurityChange = 0x00000800,

            [Description("A named stream is added to or removed from a file, or a named stream is renamed.")]
            StreamChange = 0x00200000,

            [Description("The given stream is modified through a TxF transaction.")]
            TransactedChange = 0x00400000
        }

        [Flags]
        public enum UpdateSourceFlag
        {
            Na = 0x0,

            [Description(
                "The operation adds a private data stream to a file or directory. An example might be a virus detector adding checksum information. As the virus detector modifies the item, the system generates USN records. USN_SOURCE_AUXILIARY_DATA indicates that the modifications did not change the application data.")]
            AuxiliaryData = 0x00000002,

            [Description(
                "The operation provides information about a change to the file or directory made by the operating system. A typical use is when the Remote Storage system moves data from external to local storage.")]
            DataManagement = 0x00000001,

            [Description(
                "The operation is modifying a file to match the contents of the same file which exists in another member of the replica set.")]
            ReplicationManagement = 0x00000004,

            [Description(
                "The operation is modifying a file on client systems to match the contents of the same file that exists in the cloud.")]
            ClientReplicationManagement = 0x00000008
        }

        public UsnEntry(byte[] rawBytes, long offset)
        {
            OffsetToData = offset;
            Size = BitConverter.ToInt32(rawBytes, 0);
            MajorVersion = BitConverter.ToInt16(rawBytes, 4);
            MinorVersion = BitConverter.ToInt16(rawBytes, 6);

            var frb = new byte[8];
            Buffer.BlockCopy(rawBytes, 8, frb, 0, 8);
            FileReference = new MftInformation(frb);

            var pfrb = new byte[8];
            Buffer.BlockCopy(rawBytes, 16, pfrb, 0, 8);
            ParentFileReference = new MftInformation(pfrb);

            UpdateSequenceNumber = BitConverter.ToUInt64(rawBytes, 24);

            UpdateTimestamp = DateTimeOffset.FromFileTime(BitConverter.ToInt64(rawBytes, 32)).UtcDateTime;

            UpdateReasons = (UpdateReasonFlag) BitConverter.ToUInt32(rawBytes, 40);
            UpdateSources = (UpdateSourceFlag) BitConverter.ToUInt32(rawBytes, 44);

            SecurityDescriptorId = BitConverter.ToInt32(rawBytes, 48);
            FileAttributes = (FileAttributeFlag) BitConverter.ToInt32(rawBytes, 52);

            NameSize = BitConverter.ToInt16(rawBytes, 56);
            NameOffset = BitConverter.ToInt16(rawBytes, 58);

            Name = Encoding.Unicode.GetString(rawBytes, NameOffset, NameSize);
        }

        public int Size { get; }
        public short MajorVersion { get; }
        public short MinorVersion { get; }

        public MftInformation FileReference { get; }
        public MftInformation ParentFileReference { get; }

        public ulong UpdateSequenceNumber { get; }

        public DateTimeOffset UpdateTimestamp { get; }

        public UpdateReasonFlag UpdateReasons { get; }
        public UpdateSourceFlag UpdateSources { get; }

        public int SecurityDescriptorId { get; }
        public FileAttributeFlag FileAttributes { get; }

        public short NameSize { get; }
        public short NameOffset { get; }

        public string Name { get; }

        public long OffsetToData { get;set; }

        public override string ToString()
        {
            return
                $"Offset: 0x {OffsetToData:X} Name: {Name}, Usn: {UpdateSequenceNumber}, File ref: {FileReference} parent ref: {ParentFileReference}, Timestamp: {UpdateTimestamp:yyyy/MM/dd HH:mm:ss.fffffff}, Reasons: {UpdateReasons}, File attr: {FileAttributes}";
        }

        public static string GetDescriptionFromEnumValue(Enum value)
        {
            var attribute = value.GetType()
                .GetField(value.ToString())
                .GetCustomAttributes(typeof(DescriptionAttribute), false)
                .SingleOrDefault() as DescriptionAttribute;
            return attribute == null ? value.ToString() : attribute.Description;
        }
    }
}