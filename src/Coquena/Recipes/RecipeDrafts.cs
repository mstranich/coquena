using System.Security.Cryptography;
using System.Text;

namespace Coquena.Recipes;

// Text drafts of the files of one combination, as the Editor holds them. Validation reads the
// drafts in memory with the same reader and checks as an edit from the CLI; saving writes one
// file only when the disk still holds what was opened, unless the caller chose to overwrite.
internal static class RecipeDrafts
{
    internal sealed record Diagnostic(string File, int? Line, int? Column, string Message);

    internal enum SaveResult { Saved, Unchanged, Changed, Missing }

    // The first problem the core reports for the combination with these drafts, or null.
    internal static Diagnostic? Validate(RecipeSelection selection, IReadOnlyDictionary<string, byte[]> drafts)
    {
        var captured = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in selection.Files.Concat(selection.EnvFiles))
                captured[file] = drafts.TryGetValue(file, out var draft) ? draft : RecipeYaml.ReadBytes(file);
            var candidate = new RecipeSelection(selection.Base, selection.Files, selection.Base, selection.EnvFiles) { CapturedFiles = captured };
            var recipe = RecipeReader.Read(candidate);
            if (recipe.Root.Get("service")?.Get("backend") is not null) _ = Backends.BackendIntent.FromRecipe(recipe);
            _ = Models.ModelIntent.FromRecipe(recipe);
            _ = ArgumentValidation.Validate(recipe);
            return null;
        }
        catch (RecipeException error)
        {
            // A position is shown only when it points into one of the edited files.
            var located = error.Origin.Line > 0 && captured.ContainsKey(error.Origin.File);
            var detail = error.Message[(error.Message.IndexOf("] ", StringComparison.Ordinal) is var at and >= 0 ? at + 2 : 0)..];
            return new(error.Origin.File, located ? error.Origin.Line : null, located ? error.Origin.Column : null,
                (error.Origin.Path is { Length: > 0 } path && path != "$" ? path.TrimStart('$', '.') + ": " : "") + detail);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
        {
            return new(selection.Files[0], null, null, error.Message);
        }
    }

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
