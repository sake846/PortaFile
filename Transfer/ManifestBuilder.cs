using PortaFile.Protocol;

namespace PortaFile.Transfer;

public static class ManifestBuilder
{
    private const string DefaultTransferName = "Transfer";

    public static async Task<(TransferManifest Manifest, List<string> SourceFiles)> BuildAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        var inputPaths = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (inputPaths.Count == 0)
        {
            throw new InvalidOperationException("送信対象がありません。");
        }

        var rootFileCount = inputPaths.Count(File.Exists);
        var rootFolderCount = inputPaths.Count(Directory.Exists);
        var sourceFiles = new List<string>();
        var entries = new List<FileManifestEntry>();
        var rootName = DetermineRootName(inputPaths);

        foreach (var path in inputPaths)
        {
            if (File.Exists(path))
            {
                sourceFiles.Add(path);
            }
            else if (Directory.Exists(path))
            {
                sourceFiles.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories));
            }
        }

        sourceFiles = sourceFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        for (var i = 0; i < sourceFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = sourceFiles[i];
            var info = new FileInfo(file);
            entries.Add(new FileManifestEntry
            {
                Index = i,
                RelativePath = MakeRelativePath(inputPaths, file),
                Size = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                Crc32 = await Crc32.ComputeFileAsync(file, cancellationToken)
            });
        }

        var manifest = new TransferManifest
        {
            TransferId = Guid.NewGuid(),
            RootName = SanitizeName(rootName),
            RootFileCount = rootFileCount,
            RootFolderCount = rootFolderCount,
            TotalBytes = entries.Sum(x => x.Size),
            Files = entries
        };

        return (manifest, sourceFiles);
    }

    private static string DetermineRootName(List<string> paths)
    {
        if (paths.Count == 1)
        {
            var single = paths[0];
            return Directory.Exists(single)
                ? new DirectoryInfo(single).Name
                : Path.GetFileNameWithoutExtension(single);
        }

        return DefaultTransferName;
    }

    private static string MakeRelativePath(List<string> roots, string file)
    {
        foreach (var root in roots.Where(Directory.Exists))
        {
            if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(root, file);
            }
        }

        return Path.GetFileName(file);
    }

    private static string SanitizeName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? DefaultTransferName : value;
    }
}
