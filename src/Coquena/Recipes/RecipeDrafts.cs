using System.Security.Cryptography;
using System.Text;

namespace Coquena.Recipes;

// Text drafts of the files of one combination, as the Editor holds them. Validation reads the
// drafts in memory with the same checks as verify and the CLI; saving writes one
// file only when the disk still holds what was opened, unless the caller chose to overwrite.
internal static class RecipeDrafts
{
    internal enum SaveResult { Saved, Unchanged, Changed, Missing }

    // Every problem the shared validation reports for the combination with these drafts.
    internal static IReadOnlyList<RecipeDiagnostic> Validate(RecipeSelection selection, IReadOnlyDictionary<string, byte[]> drafts) =>
        RecipeValidation.Check(selection, drafts).Problems;

    internal static byte[] Encode(string text, byte[] original)
    {
        var (_, offset) = RecipeYaml.TextEncoding(original, false);
        return original.Take(offset).Concat(new UTF8Encoding(false).GetBytes(text)).ToArray();
    }

    // Writes the draft atomically. Without overwrite, a file changed or removed since it was
    // opened is left untouched and reported, so the caller can ask what to do.
    internal static SaveResult Save(string file, byte[] opened, byte[] draft, bool overwrite)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.ToUpperInvariant())));
        using var mutex = new Mutex(false, "CoquenaRecipeEdit-" + id);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
        var temporary = Path.Combine(Path.GetDirectoryName(file)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var exists = File.Exists(file);
            if (!overwrite)
            {
                if (!exists) return SaveResult.Missing;
                if (!RecipeYaml.ReadBytes(file).AsSpan().SequenceEqual(opened)) return SaveResult.Changed;
            }
            if (exists && RecipeYaml.ReadBytes(file).AsSpan().SequenceEqual(draft)) return SaveResult.Unchanged;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(draft);
                stream.Flush(true);
            }
            if (File.Exists(file)) File.Replace(temporary, file, null);
            else File.Move(temporary, file);
            return SaveResult.Saved;
        }
        finally
        {
            try { File.Delete(temporary); }
            finally { mutex.ReleaseMutex(); }
        }
    }
}
