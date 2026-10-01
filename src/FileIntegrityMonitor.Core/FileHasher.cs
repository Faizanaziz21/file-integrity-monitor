using System.Security.Cryptography;

namespace FileIntegrityMonitor.Core;

/// <summary>Computes SHA-256 digests of files.</summary>
public static class FileHasher
{
    /// <summary>Returns the lowercase hex SHA-256 digest of the file at <paramref name="path"/>.</summary>
    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
