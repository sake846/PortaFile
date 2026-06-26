namespace PortaFile.Transfer;

public static class PathResolver
{
    private const string PartExtension = ".part";
    private const int MaxCandidateCount = 10000;

    public static string DownloadsDirectory
    {
        get
        {
            var baseDirectory = AppContext.BaseDirectory;
            var path = Path.Combine(baseDirectory, "Downloads");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string CreatePartPath(TransferManifest manifest, FileManifestEntry entry)
    {
        var safeRoot = SanitizePathPart(manifest.RootName);
        var relative = SanitizeRelativePath(entry.RelativePath);
        var targetPath = Path.Combine(DownloadsDirectory, safeRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        return targetPath + PartExtension;
    }

    public static string GetAvailableFinalPath(string partPath)
    {
        var target = partPath.EndsWith(PartExtension, StringComparison.OrdinalIgnoreCase)
            ? partPath[..^PartExtension.Length]
            : partPath;

        if (!File.Exists(target) && !Directory.Exists(target))
        {
            return target;
        }

        var directory = Path.GetDirectoryName(target)!;
        var fileName = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);

        for (var i = 1; i < MaxCandidateCount; i++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("保存先ファイル名を決定できません。");
    }

    private static string SanitizeRelativePath(string relativePath)
    {
        var parts = relativePath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizePathPart)
            .Where(x => x is not "." and not "..")
            .ToArray();

        return Path.Combine(parts);
    }

    private static string SanitizePathPart(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "_" : value;
    }
}
