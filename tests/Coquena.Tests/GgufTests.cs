using System.Security.Cryptography;
using System.Text;
using Coquena.Contracts;
using Coquena.Models;

namespace Coquena.Tests;

internal static class GgufTests
{
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-gguf-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var checks = 0;
        try
        {
            var single = Path.Combine(root, "arbitrary-name.gguf");
            File.WriteAllBytes(single, Fixture());
            Check(GgufInspection.Inspect(single).Status == "complete", "single nonconventional name");
            var first = Path.Combine(root, "model-Q4_K_M-00001-of-00002.gguf");
            var second = Path.Combine(root, "model-Q4_K_M-00002-of-00002.gguf");
            File.WriteAllBytes(first, Fixture(0, 2));
            Check(GgufInspection.Inspect(first) is { Status: "incomplete", Missing.Length: 1, Expected.Length: 2 }, "missing shard");
            Check(GgufInspection.InspectPaths([first]) is { Status: "incomplete", Missing.Length: 1, Expected.Length: 2 }, "partial selection retains expected missing");
            File.WriteAllBytes(second, Fixture(1, 2));
            Check(GgufInspection.Inspect(first).Status == "complete", "complete shards");
            Check(GgufInspection.InspectPaths([second, first]).Parts[0].Path == first, "selection ordered first shard first");
            Check(GgufInspection.InspectPaths([first, single]).Status == "invalid", "mixed units");
            Check(GgufInspection.Inspect(second).Status == "invalid", "first shard required");
            Check(GgufInspection.InspectPaths([first, first]).Status == "invalid", "duplicate paths");
            File.WriteAllBytes(second, Fixture(0, 2));
            Check(GgufInspection.Inspect(first).Status == "invalid", "wrong zero-based index");
            File.WriteAllBytes(second, Fixture(1, 3));
            Check(GgufInspection.Inspect(first).Status == "invalid", "wrong total");
            File.WriteAllBytes(second, Fixture(1, 2, "different"));
            Check(GgufInspection.Inspect(first).Status == "invalid", "contradictory architecture");
            File.WriteAllBytes(second, Fixture(1, 2, tensorTotal: 9));
            Check(GgufInspection.Inspect(first).Status == "invalid", "contradictory global tensor count");
            File.WriteAllBytes(first, Fixture(0, 2, tensorTotal: 9));
            Check(GgufInspection.Inspect(first).Status == "invalid", "sum of tensor counts");
            File.WriteAllBytes(first, Fixture(0, 2)); File.WriteAllBytes(second, Fixture(1, 2));
            File.WriteAllBytes(single, Fixture(0, 2));
            Check(GgufInspection.Inspect(single).Status == "unsupported", "nonconventional split not declared invalid");
            File.WriteAllBytes(single, Fixture(0, 0));
            Check(GgufInspection.Inspect(single).Status == "complete", "official merged GGUF disables split with zero count");
            using (var locked = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(GgufInspection.Inspect(first).Status == "read-error", "locked shard not missing");
            var bytes = Fixture();
            using (var stream = new MemoryStream(bytes.Concat(new byte[4096]).ToArray()))
            {
                GgufInspection.ReadHeader(stream);
                Check(stream.Position == bytes.Length, "does not read tensor weights or descriptors");
            }
            for (var i = 0; i < bytes.Length; i++)
            {
                try { GgufInspection.ReadHeader(new MemoryStream(bytes[..i])); throw new Exception("accepted truncated header"); }
                catch (GgufException e) { Check(e.Code == "invalid", "truncated at " + i); }
            }
            var bad = (byte[])bytes.Clone(); bad[0] = 0;
            Reject(bad, "invalid");
            bad = (byte[])bytes.Clone(); bad[4] = 1; Reject(bad, "unsupported");
            bad = (byte[])bytes.Clone(); bad[4] = 0; bad[7] = 3; Reject(bad, "unsupported");
            bad = (byte[])bytes.Clone(); Array.Fill(bad, (byte)255, 16, 8); Reject(bad, "limit");
            bad = (byte[])bytes.Clone(); Array.Fill(bad, (byte)255, 24, 8); Reject(bad, "limit");
            using (var m = new MemoryStream())
            {
                using var w = new BinaryWriter(m, Encoding.UTF8, true);
                Header(w, 2); Text(w, "general.architecture"); w.Write(8u); Text(w, "test");
                Text(w, "general.architecture"); w.Write(8u); Text(w, "test");
                Reject(m.ToArray(), "invalid");
            }
            using (var m = new MemoryStream())
            {
                using var w = new BinaryWriter(m, Encoding.UTF8, true);
                Header(w, 2); Text(w, "ignored.array"); w.Write(9u); w.Write(8u); w.Write(2ul); Text(w, "a"); Text(w, "b");
                Text(w, "general.architecture"); w.Write(8u); Text(w, "test");
                Check(GgufInspection.ReadHeader(new MemoryStream(m.ToArray())).Hints["general.architecture"] == "test", "skip array without losing offset");
            }
            using (var m = new MemoryStream())
            {
                using var w = new BinaryWriter(m, Encoding.UTF8, true);
                Header(w, 1); Text(w, "ignored.string"); w.Write(8u); w.Write(ulong.MaxValue);
                Reject(m.ToArray(), "limit");
            }
            foreach (var type in new uint[] { 7, 9, 42 })
            {
                using var m = new MemoryStream(); using var w = new BinaryWriter(m, Encoding.UTF8, true);
                Header(w, 1); Text(w, "ignored"); w.Write(type);
                if (type == 7) w.Write((byte)2);
                if (type == 9) { w.Write(0u); w.Write(1000001ul); }
                Reject(m.ToArray(), type == 7 ? "invalid" : type == 9 ? "limit" : "unsupported");
            }
            using (var m = new MemoryStream())
            {
                using var w = new BinaryWriter(m, Encoding.UTF8, true);
                Header(w, 1); Text(w, "split.count"); w.Write(4u); w.Write(2u);
                Reject(m.ToArray(), "invalid");
            }
            var version2 = Fixture(version: 2);
            Check(GgufInspection.ReadHeader(new MemoryStream(version2)).Version == 2, "v2");
            using (var m = new MemoryStream())
            {
                using var w = new BinaryWriter(m, Encoding.UTF8, true);
                Header(w, 3);
                Text(w, "split.no"); w.Write(2u); w.Write((ushort)1);
                Text(w, "split.count"); w.Write(2u); w.Write((ushort)2);
                Text(w, "split.tensors.count"); w.Write(5u); w.Write(2);
                File.WriteAllBytes(second, m.ToArray());
                Check(GgufInspection.Inspect(first).Status == "complete", "official later shard contains split keys only");
            }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { GgufInspection.ReadHeader(new MemoryStream(bytes), cancel.Token); throw new Exception("ignored cancellation"); }
            catch (OperationCanceledException) { checks++; }
            var payload = new byte[1024 * 1024 + 17]; new Random(17).NextBytes(payload);
            var reference = new ContentIdentity("sha256", "file-bytes", Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
            File.WriteAllBytes(single, payload);
            Check((await FileIntegrity.VerifyAsync(single, reference)).Status == "verified", "complete file sha");
            Check((await FileIntegrity.VerifyAsync(single, reference with { Value = new string('0', 64) })).Status == "mismatch", "incorrect digest");
            Check((await FileIntegrity.VerifyAsync(single, null)).Status == "no-reference", "no implicit baseline");
            foreach (var identity in new[] { reference with { Domain = "git-blob" }, reference with { Algorithm = "sha1" },
                reference with { Domain = "xet-file" }, reference with { Algorithm = "etag" }, reference with { Value = "short" } })
                Check((await FileIntegrity.VerifyAsync(single, identity)).Status == "no-reference", "not comparable");
            Check((await FileIntegrity.VerifyAsync(single + ".missing", reference)).Status == "read-error", "missing file hash");
            using (var locked = new FileStream(single, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check((await FileIntegrity.VerifyAsync(single, reference)).Status == "read-error", "locked hash");
            foreach (var prefix in new[] { 0, 1, 65535, 65536, payload.Length })
            {
                using var hash = new FileHashSession();
                using var source = new MemoryStream(payload);
                await hash.ReadPrefixAsync(source, prefix);
                using var target = new MemoryStream();
                target.Write(payload.AsSpan(0, prefix));
                for (var offset = prefix; offset < payload.Length; offset += 32768)
                {
                    target.Write(payload.AsSpan(offset, Math.Min(32768, payload.Length - offset)));
                    hash.Append(payload.AsSpan(offset, Math.Min(32768, payload.Length - offset)));
                }
                Check(hash.Bytes == payload.Length && FileIntegrity.Compare(hash.Finish(), reference).Status == "verified", "resumed prefix " + prefix);
                Check(target.ToArray().SequenceEqual(payload), "write and hash same complete bytes");
                try { hash.Finish(); throw new Exception("hash reused"); } catch (ObjectDisposedException) { checks++; }
            }
            using (var hash = new FileHashSession())
            {
                try { await hash.ReadPrefixAsync(new MemoryStream([1]), 2); throw new Exception("prefix truncated accepted"); }
                catch (EndOfStreamException) { checks++; }
            }
            try { await FileIntegrity.VerifyAsync(single, reference, cancel.Token); throw new Exception("hash ignored cancellation"); }
            catch (OperationCanceledException) { checks++; }
            Console.WriteLine($"OK: {checks} comprobaciones GGUF/integridad.");
        }
        finally { Directory.Delete(root, true); }
        void Check(bool value, string name) { if (!value) throw new Exception("GGUF: " + name); checks++; }
        void Reject(byte[] value, string code)
        {
            try { GgufInspection.ReadHeader(new MemoryStream(value)); throw new Exception("accepted malformed header"); }
            catch (GgufException e) { Check(e.Code == code, "header " + code); }
        }
    }
    private static void Text(BinaryWriter w, string value) { var b = Encoding.UTF8.GetBytes(value); w.Write((ulong)b.Length); w.Write(b); }
    private static void Header(BinaryWriter w, ulong keys, uint version = 3)
    { w.Write("GGUF"u8); w.Write(version); w.Write(1ul); w.Write(keys); }
    private static byte[] Fixture(int? index = null, int total = 1, string architecture = "test", int tensorTotal = 2, uint version = 3)
    {
        using var m = new MemoryStream(); using var w = new BinaryWriter(m, Encoding.UTF8, true);
        Header(w, index.HasValue ? 4ul : 1ul, version);
        Text(w, "general.architecture"); w.Write(8u); Text(w, architecture);
        if (index.HasValue)
        {
            Text(w, "split.no"); w.Write(2u); w.Write((ushort)index.Value);
            Text(w, "split.count"); w.Write(2u); w.Write((ushort)total);
            Text(w, "split.tensors.count"); w.Write(5u); w.Write(tensorTotal);
        }
        return m.ToArray();
    }
}
