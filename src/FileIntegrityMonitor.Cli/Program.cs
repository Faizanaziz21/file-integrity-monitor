using System.Runtime.InteropServices;
using FileIntegrityMonitor.Core;

namespace FileIntegrityMonitor.Cli;

internal static class Program
{
    private const int ExitClean = 0;
    private const int ExitChangesDetected = 1;
    private const int ExitError = 2;

    private static readonly object ConsoleLock = new();

    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length > 0 && args[0] is "-h" or "--help" ? ExitClean : ExitError;
        }

        var command = args[0].ToLowerInvariant();
        var directory = args[1];
        string? manifestPath = null;

        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] is "-m" or "--manifest" && i + 1 < args.Length)
            {
                manifestPath = args[++i];
            }
            else
            {
                Console.Error.WriteLine($"Unknown argument: {args[i]}");
                PrintUsage();
                return ExitError;
            }
        }

        manifestPath = Path.GetFullPath(
            manifestPath ?? Path.Combine(directory, IntegrityChecker.DefaultManifestFileName));

        try
        {
            return command switch
            {
                "baseline" => Baseline(directory, manifestPath),
                "verify" => Verify(directory, manifestPath),
                "watch" => Watch(directory),
                _ => UnknownCommand(command),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }
    }

    private static int Baseline(string directory, string manifestPath)
    {
        var checker = new IntegrityChecker(directory, [manifestPath]);
        var manifest = checker.CreateBaseline();
        manifest.Save(manifestPath);

        PrintErrors(checker);
        Console.WriteLine($"Baseline created for {checker.Root}");
        Console.WriteLine($"  Files hashed : {manifest.Files.Count}");
        Console.WriteLine($"  Manifest     : {manifestPath}");
        return ExitClean;
    }

    private static int Verify(string directory, string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Error: manifest not found at {manifestPath}. Run 'baseline' first.");
            return ExitError;
        }

        var manifest = Manifest.Load(manifestPath);
        var checker = new IntegrityChecker(directory, [manifestPath]);
        var report = checker.Verify(manifest);

        PrintErrors(checker);
        Console.WriteLine($"Verifying {checker.Root} against baseline from {manifest.CreatedUtc:u}");

        foreach (var path in report.Added)
        {
            WriteColored(ConsoleColor.Green, $"  [ADDED]    {path}");
        }

        foreach (var path in report.Modified)
        {
            WriteColored(ConsoleColor.Yellow, $"  [MODIFIED] {path}");
        }

        foreach (var path in report.Deleted)
        {
            WriteColored(ConsoleColor.Red, $"  [DELETED]  {path}");
        }

        if (report.IsClean)
        {
            Console.WriteLine($"OK: all {manifest.Files.Count} files match the baseline.");
            return ExitClean;
        }

        Console.WriteLine(
            $"FAILED: {report.TotalChanges} change(s) - " +
            $"{report.Added.Count} added, {report.Modified.Count} modified, {report.Deleted.Count} deleted.");
        return ExitChangesDetected;
    }

    private static int Watch(string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Directory not found: {root}");
        }

        using var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        watcher.Created += (_, e) => Log(ConsoleColor.Green, "CREATED", Relative(root, e.FullPath));
        watcher.Changed += (_, e) => Log(ConsoleColor.Yellow, "CHANGED", Relative(root, e.FullPath));
        watcher.Deleted += (_, e) => Log(ConsoleColor.Red, "DELETED", Relative(root, e.FullPath));
        watcher.Renamed += (_, e) => Log(
            ConsoleColor.Cyan, "RENAMED", $"{Relative(root, e.OldFullPath)} -> {Relative(root, e.FullPath)}");
        watcher.Error += (_, e) => Log(
            ConsoleColor.Magenta, "ERROR", e.GetException().Message + " (some events may have been missed)");

        using var stop = new ManualResetEventSlim();

        void OnSignal(PosixSignalContext context)
        {
            context.Cancel = true;
            stop.Set();
        }

        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);

        watcher.EnableRaisingEvents = true;
        Console.WriteLine($"Watching {root} (Ctrl+C to stop)...");
        stop.Wait();
        Console.WriteLine("Stopped.");
        return ExitClean;
    }

    private static void Log(ConsoleColor color, string kind, string message) =>
        WriteColored(color, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind,-8} {message}");

    private static void WriteColored(ConsoleColor color, string line)
    {
        lock (ConsoleLock)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = previous;
        }
    }

    private static string Relative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private static void PrintErrors(IntegrityChecker checker)
    {
        foreach (var (path, error) in checker.Errors)
        {
            Console.Error.WriteLine($"  [UNREADABLE] {path}: {error}");
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return ExitError;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            File Integrity Monitor (SHA-256)

            Usage:
              fim baseline <dir> [--manifest <path>]   Hash every file under <dir> and save a JSON manifest
              fim verify   <dir> [--manifest <path>]   Re-hash <dir> and report Added/Modified/Deleted files
              fim watch    <dir>                       Log create/change/delete/rename events live

            The manifest defaults to <dir>/.fim-manifest.json (excluded from hashing).

            Exit codes: 0 = clean, 1 = changes detected (verify), 2 = error.
            """);
    }
}
