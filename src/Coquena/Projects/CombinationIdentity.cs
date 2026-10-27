using System.Security.Cryptography;
using System.Text;
using Coquena.Recipes;

namespace Coquena.Projects;

// Selection identity is textual and portable. Physical identity belongs to ScopeContext.
public sealed record CombinationIdentity(string Id, string[] Files, string[] EnvFiles, bool Private)
{
    public static CombinationIdentity FromSelection(RecipeSelection selection, bool privateSelection = false)
    {
        var local = privateSelection || selection.EnvFiles.Count != 0;
        string Normalize(string path)
        {
            var relative = Path.GetRelativePath(selection.Base, path).Replace('\\', '/');
            if (Path.IsPathRooted(relative))
                local = true;
            return relative;
        }
        var files = selection.Files.Select(Normalize).ToArray();
        var env = selection.EnvFiles.Select(Normalize).ToArray();
        return new(Hash(files, env), files, env, local);
    }

    public static string Hash(IReadOnlyList<string> files, IReadOnlyList<string> envFiles)
    {
        // JSON encoders may escape supplementary Unicode even with their relaxed
        // encoder. The selection contract requires literal Unicode in UTF-8.
        var text = new StringBuilder("{\"files\":[");
        Write(files);
        text.Append("],\"env-files\":[");
        Write(envFiles);
        text.Append("]}");
        return Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(text.ToString())));
        void Write(IReadOnlyList<string> paths)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                if (i != 0) text.Append(',');
                text.Append('"');
                foreach (var c in paths[i])
                    text.Append(c switch
                    {
                        '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                        < ' ' => "\\u" + ((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture), _ => c.ToString()
                    });
                text.Append('"');
            }
        }
    }
}
