using System.Text;

namespace Coquena;

// Reads documents that another thread or process may replace at the same time. File.ReadAllBytes
// opens without delete sharing, and on Windows a reader opened that way makes File.Replace and
// File.Move of the same document fail with a sharing violation. Sharing delete lets the writer
// swap the file while this handle keeps reading the previous, complete version.
internal static class SharedFile
{
    public static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public static byte[] ReadAllBytes(string path)
    {
        using var stream = Open(path);
        using var bytes = new MemoryStream(checked((int)stream.Length));
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    public static string ReadAllText(string path)
    {
        using var reader = new StreamReader(Open(path), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
