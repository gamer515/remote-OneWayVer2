using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

/// <summary>Full streaming SHA256. Windows uses CNG instead of Mono's slower managed implementation.</summary>
public static class LocalStoryModelHash
{
    private const int BufferSize = 1024 * 1024;
    public static string ProviderName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows CNG SHA256" : "Managed SHA256";

    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm, string id, string implementation, uint flags);
    [DllImport("bcrypt.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int BCryptCreateHash(IntPtr algorithm, out IntPtr hash, IntPtr hashObject, uint objectLength, IntPtr secret, uint secretLength, uint flags);
    [DllImport("bcrypt.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int BCryptHashData(IntPtr hash, [In] byte[] input, uint length, uint flags);
    [DllImport("bcrypt.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int BCryptFinishHash(IntPtr hash, [Out] byte[] output, uint length, uint flags);
    [DllImport("bcrypt.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int BCryptDestroyHash(IntPtr hash);
    [DllImport("bcrypt.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm, uint flags);

    private static void Check(int status)
    {
        if (status < 0) throw new CryptographicException("Windows SHA256 실패: 0x" + unchecked((uint)status).ToString("X8"));
    }
    public static string FileHash(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        // Disallow writers while reading. Neither a sidecar cache nor a sampled checksum is trusted.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        return Compute(stream, token);
    }
    public static string Compute(Stream stream, CancellationToken token, bool windowsProvider = true)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        token.ThrowIfCancellationRequested();
        var buffer = new byte[BufferSize];
        byte[] digest;
        if (windowsProvider && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            IntPtr algorithm = IntPtr.Zero, hash = IntPtr.Zero;
            try
            {
                Check(BCryptOpenAlgorithmProvider(out algorithm, "SHA256", null, 0));
                // Windows 7+ owns this object's allocation until BCryptDestroyHash.
                // https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptcreatehash
                Check(BCryptCreateHash(algorithm, out hash, IntPtr.Zero, 0, IntPtr.Zero, 0, 0));
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    Check(BCryptHashData(hash, buffer, (uint)count, 0));
                }
                token.ThrowIfCancellationRequested();
                digest = new byte[32];
                Check(BCryptFinishHash(hash, digest, (uint)digest.Length, 0));
            }
            finally
            {
                if (hash != IntPtr.Zero) BCryptDestroyHash(hash);
                if (algorithm != IntPtr.Zero) BCryptCloseAlgorithmProvider(algorithm, 0);
            }
        }
        else
        {
            using var sha = SHA256.Create();
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, count, buffer, 0);
            }
            token.ThrowIfCancellationRequested();
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            digest = sha.Hash;
        }
        return BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
    }
}

[Serializable] public sealed class LocalStoryModelVerification
{
    public string provider, modelPath;
    public bool cached;
    public long bytesHashed;
    public double seconds;
}
