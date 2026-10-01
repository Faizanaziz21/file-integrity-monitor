using FileIntegrityMonitor.Core;

namespace FileIntegrityMonitor.Tests;

public sealed class IntegrityCheckerTests : IDisposable
{
    private readonly string _root;

    public IntegrityCheckerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fim-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Write("readme.txt", "hello");
        Write("config/app.json", "{ \"debug\": false }");
        Write("config/nested/deep.bin", "deep");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void NoChanges_ReportIsClean()
    {
        var checker = new IntegrityChecker(_root);
        var baseline = checker.CreateBaseline();

        var report = checker.Verify(baseline);

        Assert.True(report.IsClean);
        Assert.Equal(0, report.TotalChanges);
    }

    [Fact]
    public void Baseline_HashesAllFilesRecursivelyWithRelativeKeys()
    {
        var hashes = new IntegrityChecker(_root).ComputeHashes();

        Assert.Equal(
            new[] { "config/app.json", "config/nested/deep.bin", "readme.txt" },
            hashes.Keys);
        // SHA-256("hello")
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            hashes["readme.txt"]);
    }

    [Fact]
    public void ModifiedFile_IsDetected()
    {
        var checker = new IntegrityChecker(_root);
        var baseline = checker.CreateBaseline();

        Write("config/app.json", "{ \"debug\": true }");
        var report = checker.Verify(baseline);

        Assert.Equal(new[] { "config/app.json" }, report.Modified);
        Assert.Empty(report.Added);
        Assert.Empty(report.Deleted);
    }

    [Fact]
    public void AddedFile_IsDetected()
    {
        var checker = new IntegrityChecker(_root);
        var baseline = checker.CreateBaseline();

        Write("config/nested/new.dll", "payload");
        var report = checker.Verify(baseline);

        Assert.Equal(new[] { "config/nested/new.dll" }, report.Added);
        Assert.Empty(report.Modified);
        Assert.Empty(report.Deleted);
    }

    [Fact]
    public void DeletedFile_IsDetected()
    {
        var checker = new IntegrityChecker(_root);
        var baseline = checker.CreateBaseline();

        File.Delete(Path.Combine(_root, "readme.txt"));
        var report = checker.Verify(baseline);

        Assert.Equal(new[] { "readme.txt" }, report.Deleted);
        Assert.Empty(report.Added);
        Assert.Empty(report.Modified);
    }

    [Fact]
    public void AddModifyDeleteTogether_AreAllDetected()
    {
        var checker = new IntegrityChecker(_root);
        var baseline = checker.CreateBaseline();

        Write("added.txt", "new");
        Write("readme.txt", "tampered");
        Directory.Delete(Path.Combine(_root, "config", "nested"), recursive: true);
        var report = checker.Verify(baseline);

        Assert.Equal(new[] { "added.txt" }, report.Added);
        Assert.Equal(new[] { "readme.txt" }, report.Modified);
        Assert.Equal(new[] { "config/nested/deep.bin" }, report.Deleted);
        Assert.Equal(3, report.TotalChanges);
        Assert.False(report.IsClean);
    }

    [Fact]
    public void TouchWithoutContentChange_IsNotReported()
    {
        var checker = new IntegrityChecker(_root);
        var baseline = checker.CreateBaseline();

        File.SetLastWriteTimeUtc(Path.Combine(_root, "readme.txt"), DateTime.UtcNow.AddDays(1));

        Assert.True(checker.Verify(baseline).IsClean);
    }

    [Fact]
    public void Manifest_RoundTripsThroughJson_AndManifestFileIsExcluded()
    {
        var manifestPath = Path.Combine(_root, IntegrityChecker.DefaultManifestFileName);
        var checker = new IntegrityChecker(_root, [manifestPath]);
        checker.CreateBaseline().Save(manifestPath);

        var loaded = Manifest.Load(manifestPath);
        var report = checker.Verify(loaded);

        Assert.Equal(3, loaded.Files.Count);
        Assert.DoesNotContain(IntegrityChecker.DefaultManifestFileName, loaded.Files.Keys);
        Assert.True(report.IsClean);
    }

    [Fact]
    public void Compare_IsPureAndOrdered()
    {
        var baseline = new Dictionary<string, string> { ["b"] = "1", ["a"] = "1", ["gone"] = "x" };
        var current = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1", ["z"] = "n", ["c"] = "n" };

        var report = IntegrityChecker.Compare(baseline, current);

        Assert.Equal(new[] { "c", "z" }, report.Added);
        Assert.Equal(new[] { "b" }, report.Modified);
        Assert.Equal(new[] { "gone" }, report.Deleted);
    }

    [Fact]
    public void MissingDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => new IntegrityChecker(Path.Combine(_root, "does-not-exist")));
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
