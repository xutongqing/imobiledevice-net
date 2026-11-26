namespace iMobileDevice.Managed.Afc;

public enum AfcErrorCode : ulong
{
    Success = 0,
    UnknownError = 1,
    OpHeaderInvalid = 2,
    NoResources = 3,
    ReadError = 4,
    WriteError = 5,
    UnknownPacketType = 6,
    InvalidArg = 7,
    ObjectNotFound = 8,
    ObjectIsDir = 9,
    PermDenied = 10,
    ServiceNotConnected = 11,
    OpTimeout = 12,
    TooMuchData = 13,
    EndOfData = 14,
    OpNotSupported = 15,
    ObjectExists = 16,
    ObjectBusy = 17,
    NoSpaceLeft = 18,
    OpWouldBlock = 19,
    IoError = 20,
    OpInterrupted = 21,
    OpInProgress = 22,
    InternalError = 23,
}

public enum AfcOperation : ulong
{
    Status = 0x00000001,
    Data = 0x00000002,
    ReadDir = 0x00000003,
    ReadFile = 0x00000004,
    WriteFile = 0x00000005,
    WritePart = 0x00000006,
    Truncate = 0x00000007,
    RemovePath = 0x00000008,
    MakeDir = 0x00000009,
    GetFileInfo = 0x0000000A,
    GetDeviceInfo = 0x0000000B,
    WriteFileAtom = 0x0000000C,
    FileRefOpen = 0x0000000D,
    FileRefOpenResult = 0x0000000E,
    FileRefRead = 0x0000000F,
    FileRefWrite = 0x00000010,
    FileRefSeek = 0x00000011,
    FileRefTell = 0x00000012,
    FileRefTellResult = 0x00000013,
    FileRefClose = 0x00000014,
    FileRefSetFileSize = 0x00000015,
    GetConnectionInfo = 0x00000016,
    SetConnectionOptions = 0x00000017,
    RenamePath = 0x00000018,
    SetFsBlockSize = 0x00000019,
    SetSocketBlockSize = 0x0000001A,
    FileRefLock = 0x0000001B,
    MakeLink = 0x0000001C,
    GetFileHash = 0x0000001D,
    SetModTime = 0x0000001E,
    GetFileHashRange = 0x0000001F,
    RemovePathAndContents = 0x00000022,
}

public enum AfcFileOpenMode : ulong
{
    ReadOnly = 1,
    ReadWrite = 2,
    WriteOnly = 3,
    WriteRead = 4,
    Append = 5,
    ReadAppend = 6,
}

public sealed record AfcFileInfo(
    long Size,
    long Blocks,
    string FileType,
    DateTimeOffset? CreationTime,
    DateTimeOffset? ModifyTime,
    IReadOnlyDictionary<string, string> RawProperties)
{
    public bool IsDirectory => string.Equals(FileType, "S_IFDIR", StringComparison.Ordinal);
    public bool IsRegularFile => string.Equals(FileType, "S_IFREG", StringComparison.Ordinal);
    public bool IsSymbolicLink => string.Equals(FileType, "S_IFLNK", StringComparison.Ordinal);
}

public sealed class AfcException : Exception
{
    public AfcException(string message, AfcErrorCode errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public AfcErrorCode ErrorCode { get; }
}
