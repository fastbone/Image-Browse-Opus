using System.IO;
using System.Security.Cryptography;

namespace ImageBrowse.Services;

public static class ContentHashService
{
    private const int SampleSize = 65536; // 64KB

    public static string ComputeHash(string filePath, long fileSize)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var buffer = new byte[Math.Min(SampleSize, stream.Length)];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        sha.TransformBlock(buffer, 0, bytesRead, null, 0);

        var sizeBytes = BitConverter.GetBytes(fileSize);
        sha.TransformFinalBlock(sizeBytes, 0, sizeBytes.Length);

        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}
