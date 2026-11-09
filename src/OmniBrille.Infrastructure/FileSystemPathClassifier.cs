using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OmniBrille.Infrastructure;

public enum FileSystemPathKind
{
    Ordinary,
    CloudPlaceholder,
    BlockedReparsePoint,
    Unavailable,
}

/// <summary>Distinguishes Cloud Files placeholders from paths that redirect filesystem authority.</summary>
public static class FileSystemPathClassifier
{
    private const uint CloudTag = 0x9000001A;
    private const uint CloudVariantMask = 0x0000F000;
    private const uint NameSurrogateFlag = 0x20000000;
    private const uint GetReparsePointControlCode = 0x000900A8;
    private const uint OpenExisting = 3;
    private const uint ShareReadWriteDelete = 7;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenNoRecall = 0x00100000;
    private const int ReparseHeaderSize = 8;
    private const int MaximumReparseBufferSize = 16 * 1024;

    public static FileSystemPathKind Classify(string path)
    {
        try
        {
            return Classify(path, File.GetAttributes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return FileSystemPathKind.Unavailable;
        }
    }

    /// <summary>The attributes must be freshly obtained from this path; no content stream is opened.</summary>
    public static FileSystemPathKind Classify(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return FileSystemPathKind.Ordinary;
        }

        // Unix reparse points are links. Windows also uses the attribute for ordinary
        // Cloud Files placeholders, so inspect the actual tag rather than guessing from a path name.
        return OperatingSystem.IsWindows() && IsSupportedCloudReparseTag(ReadWindowsReparseTag(path))
            ? FileSystemPathKind.CloudPlaceholder
            : FileSystemPathKind.BlockedReparsePoint;
    }

    public static bool IsSupportedCloudReparseTag(uint? tag) =>
        tag is { } value && (value & NameSurrogateFlag) == 0 && (value & ~CloudVariantMask) == CloudTag;

    private static uint? ReadWindowsReparseTag(string path)
    {
        // Microsoft documents CLOUD and CLOUD_1..F in MS-FSCC 2.1.2.1:
        // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/c8e77b37-3909-4fe6-a4ea-2b9d423b1ee4
        // Open the reparse point itself with zero data access. OPEN_EXISTING and
        // FSCTL_GET_REPARSE_POINT neither mutate the entry nor read its file contents.
        using var handle = CreateFileW(path, 0, ShareReadWriteDelete, IntPtr.Zero, OpenExisting,
            OpenReparsePoint | BackupSemantics | OpenNoRecall, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(MaximumReparseBufferSize);
        try
        {
            if (!DeviceIoControl(handle, GetReparsePointControlCode, IntPtr.Zero, 0,
                    buffer, MaximumReparseBufferSize, out var bytesReturned, IntPtr.Zero) ||
                bytesReturned < ReparseHeaderSize || bytesReturned > MaximumReparseBufferSize ||
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4)) > bytesReturned - ReparseHeaderSize)
            {
                return null;
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        int inputBufferSize,
        [Out] byte[] outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
