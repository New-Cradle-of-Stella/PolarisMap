namespace AicTmapToPmap;

internal static class WorkspaceDefaults
{
    internal static string? FindProjectsRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Polaris"))) return directory.FullName;
                directory = directory.Parent;
            }
        }
        return null;
    }

    internal static string? ResolveGameRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return Path.GetFullPath(explicitRoot);
        string? projects = FindProjectsRoot();
        if (projects == null) return null;
        string marker = Path.Combine(projects, "Polaris", "aic_path.txt");
        return File.Exists(marker) ? File.ReadAllText(marker).Trim().Trim('"') : null;
    }
}
