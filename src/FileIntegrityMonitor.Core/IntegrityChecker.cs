namespace FileIntegrityMonitor.Core;

/// <summary>Builds SHA-256 baselines for a directory tree and compares them against the current state.</summary>
public sealed class IntegrityChecker
{
    /// <summary>Default manifest file name, written to the root of the monitored directory.</summary>
    public const string DefaultManifestFileName = ".fim-manifest.json";

    private readonly HashSet<string> _excludedFullPaths;
    private readonly List<(string, string)> _errors = [];

    /// <param name="root">Directory to scan recursively.</param>
    /// <param name="excludedPaths">Files to skip (e.g. the manifest itself when it lives inside <paramref name="root"/>).</param>
    public IntegrityChecker(string root, IEnumerable<string>? excludedPaths = null)
    {
        Root = Path.GetFullPath(root);
        if (!Directory.Exists(Root))
        {
            throw new DirectoryNotFoundException($"Directory not found: {Root}");
        }

        _excludedFullPaths = new HashSet<string>(
            (excludedPaths ?? []).Select(Path.GetFullPath),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public string Root { get; }

    /// <summary>Files that could not be read during the last <see cref="ComputeHashes"/> call.</summary>
    public IReadOnlyList<(string RelativePath, string Error)> Errors => _errors;

    /// <summary>
    /// Walks <see cref="Root"/> recursively (including hidden files, skipping symlinks)
    /// and returns relative path ('/'-separated) -> SHA-256 hex digest.
    /// </summary>
    public SortedDictionary<string, string> ComputeHashes()
    {
        _errors.Clear();
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var fullPath in Directory.EnumerateFiles(Root, "*", options))
        {
            if (_excludedFullPaths.Contains(fullPath))
            {
                continue;
            }

            var relative = ToRelativeKey(fullPath);
            try
            {
                hashes[relative] = FileHasher.ComputeSha256(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _errors.Add((relative, ex.Message));
            }
        }

        return hashes;
    }

    /// <summary>Hashes the directory and wraps the result in a <see cref="Manifest"/>.</summary>
    public Manifest CreateBaseline() => new()
    {
        Root = Root,
        CreatedUtc = DateTimeOffset.UtcNow,
        Files = ComputeHashes(),
    };

    /// <summary>Re-hashes the directory and compares it with <paramref name="baseline"/>.</summary>
    public IntegrityReport Verify(Manifest baseline) => Compare(baseline.Files, ComputeHashes());

    /// <summary>Pure comparison of two path -> hash maps.</summary>
    public static IntegrityReport Compare(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> current)
    {
        var added = new List<string>();
        var modified = new List<string>();
        var deleted = new List<string>();

        foreach (var (path, hash) in current)
        {
            if (!baseline.TryGetValue(path, out var baselineHash))
            {
                added.Add(path);
            }
            else if (!string.Equals(hash, baselineHash, StringComparison.OrdinalIgnoreCase))
            {
                modified.Add(path);
            }
        }

        foreach (var path in baseline.Keys)
        {
            if (!current.ContainsKey(path))
            {
                deleted.Add(path);
            }
        }

        added.Sort(StringComparer.Ordinal);
        modified.Sort(StringComparer.Ordinal);
        deleted.Sort(StringComparer.Ordinal);
        return new IntegrityReport(added, modified, deleted);
    }

    private string ToRelativeKey(string fullPath) =>
        Path.GetRelativePath(Root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}
