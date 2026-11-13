namespace Coquena.Projects;

public enum UsageStatus { InUse, Protected, Free, External, Kept }

// One installation known by a scope inventory, wherever its files live.
public sealed record UsageResource(string Kind, string Name, long Bytes, UsageStatus Status);

public sealed record ScopeUsage(ScopeContext Scope, IReadOnlyList<UsageResource> Resources, long Downloads, long Logs)
{
    public long Total => Resources.Sum(r => r.Bytes) + Downloads + Logs;
    public long Free => Resources.Where(r => r.Status == UsageStatus.Free).Sum(r => r.Bytes);
}

// Skipped lists known projects that could not be read; their references are unknown.
public sealed record UsageSurvey(IReadOnlyList<ScopeUsage> Scopes, IReadOnlyList<string> Skipped);

public static class DiskUsage
{
    // Temporary downloads and logs are folders of the scope; reading never creates them.
    public static long FolderSize(ScopeContext scope, string folder)
    {
        var path = Path.Combine(scope.Root, folder);
        return Directory.Exists(path) ? Size(new DirectoryInfo(path)) : 0;
    }

    private static long Size(FileSystemInfo entry)
    {
        try
        {
            // Links are not followed: their target belongs to someone else.
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return 0;
            if (entry is FileInfo file) return file.Length;
            long total = 0;
            foreach (var child in ((DirectoryInfo)entry).EnumerateFileSystemInfos()) total += Size(child);
            return total;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }
}
