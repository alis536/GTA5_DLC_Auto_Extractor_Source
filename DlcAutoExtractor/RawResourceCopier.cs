using System.Buffers;
using System.Security.Cryptography;

namespace DlcAutoExtractor;

/// <summary>
/// Copies the exact on-disk RSC7/RSC8 bytes of a GTA V resource from a physical RPF.
/// Handles nested archives by taking the archive's StartPos in the physical file.
/// This MUST NOT be used for individually encrypted resource entries.
/// </summary>
public static class RawResourceCopier
{
    public static (long Bytes, string Sha256) CopyOriginal(
        string physicalRpfPath, long archiveStartPos, long entryFileOffset,
        long entryFileSize, string destinationTempFile, bool verifyOutput)
    {
        if (archiveStartPos < 0 || entryFileOffset < 0 || entryFileSize < 16)
            throw new InvalidDataException("Invalid resource location or size.");

        long start = checked(archiveStartPos + checked(entryFileOffset * 512L));
        long end = checked(start + entryFileSize);

        using var source = new FileStream(physicalRpfPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        if (end > source.Length)
            throw new InvalidDataException($"Resource exceeds the physical RPF: offset={start}, bytes={entryFileSize}, file={source.Length}.");
        source.Position = start;
        Span<byte> header = stackalloc byte[4];
        source.ReadExactly(header);
        string signature = System.Text.Encoding.ASCII.GetString(header);
        if (signature != "RSC7" && signature != "RSC8")
            throw new InvalidDataException($"Resource at offset {start} has unexpected header {Convert.ToHexString(header)} (not RSC7/RSC8).");
        source.Position = start;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            using var output = new FileStream(destinationTempFile, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 1024 * 1024, FileOptions.SequentialScan);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long remain = entryFileSize;
            while (remain > 0)
            {
                int request = (int)Math.Min(buffer.Length, remain);
                int n = source.Read(buffer, 0, request);
                if (n <= 0) throw new EndOfStreamException("Unexpected end of source RPF while copying resource.");
                output.Write(buffer, 0, n);
                sha.AppendData(buffer, 0, n);
                remain -= n;
            }
            output.Flush(flushToDisk: true);
            string originalHash = Convert.ToHexString(sha.GetHashAndReset());
            output.Dispose();
            if (verifyOutput)
            {
                using var check = File.OpenRead(destinationTempFile);
                string writtenHash = Convert.ToHexString(SHA256.HashData(check));
                if (!string.Equals(originalHash, writtenHash, StringComparison.Ordinal))
                    throw new IOException("SHA256 verification failed: output resource is not byte-identical to the RPF entry.");
            }
            return (entryFileSize, originalHash);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public static string WriteDecodedBytes(byte[] data, string destinationTempFile, bool verifyOutput)
    {
        string expectedHash = Convert.ToHexString(SHA256.HashData(data));
        using (var fs = new FileStream(destinationTempFile, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, bufferSize: 1024 * 1024))
        {
            fs.Write(data);
            fs.Flush(flushToDisk: true);
        }
        if (verifyOutput)
        {
            using var check = File.OpenRead(destinationTempFile);
            string actualHash = Convert.ToHexString(SHA256.HashData(check));
            if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
                throw new IOException("SHA256 verification failed: the saved file differs from decoded data.");
        }
        return expectedHash;
    }
}
