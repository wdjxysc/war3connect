namespace War3Connect.Agent;

public static class GamePaths
{
    // Resolve Windows map names on case-sensitive filesystems. Reject ambiguous names and links.
    public static string? ResolveMap(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length < 2 || !parts[0].Equals("Maps", StringComparison.OrdinalIgnoreCase)
            || parts.Any(p => p.Length == 0 || p is "." or ".." || p.Contains(':'))) return null;
        string current = Path.GetFullPath(root);
        foreach (var part in parts)
        {
            if (!Directory.Exists(current)) return null;
            var matches = Directory.EnumerateFileSystemEntries(current)
                .Where(p => Path.GetFileName(p).Equals(part, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length != 1) return null;
            current = matches[0];
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
        }
        return File.Exists(current) ? current : null;
    }
}
