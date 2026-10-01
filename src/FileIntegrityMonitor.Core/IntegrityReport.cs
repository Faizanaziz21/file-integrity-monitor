namespace FileIntegrityMonitor.Core;

/// <summary>Differences between a baseline and the current state of a directory.</summary>
public sealed record IntegrityReport(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Deleted)
{
    public bool IsClean => Added.Count == 0 && Modified.Count == 0 && Deleted.Count == 0;

    public int TotalChanges => Added.Count + Modified.Count + Deleted.Count;
}
