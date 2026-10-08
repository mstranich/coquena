using System.Security.Cryptography;
using Coquena.Contracts;

namespace Coquena.Models;

public sealed record IntegrityResult(string Status, ContentIdentity? Observed, string? Diagnostic);

// A single session covers the existing prefix and every newly written byte. Never serialize
// SHA state or trust a checkpoint digest as a substitute for reading the prefix.
public sealed class FileHashSession : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool finished;
    public long Bytes { get; private set; }
    public void Append(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(finished, this);
        hash.AppendData(bytes); Bytes = checked(Bytes + bytes.Length);
    }
    public async Task ReadPrefixAsync(Stream prefix, long length, CancellationToken cancellation = default)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        var buffer = new byte[65536];
        while (length > 0)
        {
            var n = await prefix.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)), cancellation);
            if (n == 0) throw new EndOfStreamException("Prefijo reanudado truncado.");
            Append(buffer.AsSpan(0, n)); length -= n;
        }
    }
    public ContentIdentity Finish()
    {
        ObjectDisposedException.ThrowIf(finished, this);
        finished = true;
        return new("sha256", "file-bytes", Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
    public void Dispose() { finished = true; hash.Dispose(); }
}

public static class FileIntegrity
{
    public static bool Comparable(ContentIdentity? reference) => reference is
        { Algorithm: "sha256", Domain: "file-bytes", Value.Length: 64 } &&
        reference.Value.All(Uri.IsHexDigit);

    // No reference is created or replaced here. Non-comparable identities are not mismatches.
    public static IntegrityResult Compare(ContentIdentity observed, ContentIdentity? reference)
    {
        if (!Comparable(observed)) throw new ArgumentException("SHA-256 de bytes completo requerido.");
        return !Comparable(reference) ? new("no-reference", observed, "Sin referencia SHA-256 comparable de los bytes.") :
            new(string.Equals(observed.Value, reference!.Value, StringComparison.OrdinalIgnoreCase) ? "verified" : "mismatch", observed, null);
    }

    public static async Task<IntegrityResult> VerifyAsync(string path, ContentIdentity? reference,
        CancellationToken cancellation = default)
    {
        try
        {
            var before = new FileInfo(path);
            var size = before.Length; var date = before.LastWriteTimeUtc;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var session = new FileHashSession();
            await session.ReadPrefixAsync(stream, size, cancellation);
            cancellation.ThrowIfCancellationRequested();
            before.Refresh();
            if (!before.Exists || before.Length != size || before.LastWriteTimeUtc != date || stream.Length != size)
                return new("changed-during-read", null, "El archivo cambió durante la lectura; resultado no concluyente.");
            return Compare(session.Finish(), reference);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return new("read-error", null, e.Message); }
    }
}
