using System.Buffers;
using System.IO;
using System.Security.Cryptography;

namespace ImageBrowse.Services;

public static class ContentHashService
{
    private const int SampleSize = 65536; // 64KB

    public static string ComputeHash(string filePath, long fileSize)
    {
        int sampleLen = (int)Math.Min(SampleSize, fileSize);
        int totalLen = sampleLen + sizeof(long);
        var buffer = ArrayPool<byte>.Shared.Rent(totalLen);
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int bytesRead = stream.Read(buffer, 0, sampleLen);
            BitConverter.TryWriteBytes(buffer.AsSpan(bytesRead), fileSize);
            var hash = SHA256.HashData(buffer.AsSpan(0, bytesRead + sizeof(long)));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
