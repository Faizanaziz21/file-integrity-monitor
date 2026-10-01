# file-integrity-monitor

A small C#/.NET 8 file integrity monitor (`fim`). It records a SHA-256 baseline of a directory tree, checks the tree against that baseline for added, modified and deleted files, and can watch a directory live with `FileSystemWatcher`.

## Project layout

```
src/FileIntegrityMonitor.Core/    Hashing + comparison library (IntegrityChecker, Manifest, FileHasher)
src/FileIntegrityMonitor.Cli/     Command-line front end (builds to fim.dll)
tests/FileIntegrityMonitor.Tests/ xUnit tests for the core library
```

## Build and test

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet test
```

Run the CLI with `dotnet run --project src/FileIntegrityMonitor.Cli -- <command> <dir>`. You can also publish it and call `fim` directly:

```bash
dotnet publish src/FileIntegrityMonitor.Cli -c Release -o out
./out/fim verify /srv/www
```

## Usage

```
fim baseline <dir> [--manifest <path>]   Hash every file under <dir> and save a JSON manifest
fim verify   <dir> [--manifest <path>]   Re-hash <dir> and report Added/Modified/Deleted files
fim watch    <dir>                       Log create/change/delete/rename events live
```

| Exit code | Meaning |
|-----------|---------|
| `0` | Success, or `verify` found no changes |
| `1` | `verify` detected changes |
| `2` | Error (bad arguments, missing directory or manifest, unreadable manifest) |

Because the exit codes are fixed, `verify` can run from cron, CI or a monitoring check.

### Notes

- **Manifest location.** The manifest defaults to `<dir>/.fim-manifest.json`, and that file is excluded from hashing. Anyone who can change the files can also rewrite a manifest stored next to them, so for real tamper detection keep it somewhere else with `--manifest` (a read-only mount, a separate host, or a copy you check in).
- **What gets scanned.** The scan is recursive and includes hidden files. Symbolic links are skipped, which avoids directory cycles. Paths are stored relative to `<dir>` with `/` separators, so a manifest can be checked on another machine.
- **Detection is by content.** A file only counts as modified when its SHA-256 changes, so a `touch` that only updates the timestamp is not reported.
- **Unreadable files.** Files that can't be read (permissions, locks) are listed on stderr as `[UNREADABLE]` and the scan continues.

## Sample output

### `baseline`

```
$ fim baseline demo
Baseline created for /home/me/demo
  Files hashed : 4
  Manifest     : /home/me/demo/.fim-manifest.json
```

The manifest it writes:

```json
{
  "algorithm": "SHA-256",
  "root": "/home/me/demo",
  "createdUtc": "2026-10-01T18:36:34.8571842+00:00",
  "files": {
    "bin/tool.dll": "73cb3858a687a8494ca3323053016282f3dad39d42cf62ca4e79dda2aac7d9ac",
    "config/app.json": "ca3d163bab055381827226140568f3bef7eaac187cebd76878e0b63e9e442356",
    "config/old.log": "9b75290f6a6359a2a3471022cbba4b724e45105b313ae8f6c103a2f79e82a857",
    "readme.txt": "98ea6e4f216f2fb4b69fff9b3a44842c38686ca685f3f55dc48c5d3fb1107be4"
  }
}
```

### `verify` with no changes

```
$ fim verify demo
Verifying /home/me/demo against baseline from 2026-10-01 18:36:34Z
OK: all 4 files match the baseline.
$ echo $?
0
```

### `verify` after tampering

```
$ echo '{"debug":true}' > demo/config/app.json
$ echo evil > demo/bin/backdoor.dll
$ rm demo/config/old.log
$ fim verify demo
Verifying /home/me/demo against baseline from 2026-10-01 18:36:34Z
  [ADDED]    bin/backdoor.dll
  [MODIFIED] config/app.json
  [DELETED]  config/old.log
FAILED: 3 change(s) - 1 added, 1 modified, 1 deleted.
$ echo $?
1
```

### `watch`

```
$ fim watch demo
Watching /home/me/demo (Ctrl+C to stop)...
[2026-10-01 18:38:11] CREATED  logs
[2026-10-01 18:38:11] CREATED  logs/new.txt
[2026-10-01 18:38:11] CHANGED  logs/new.txt
[2026-10-01 18:38:11] CHANGED  logs/new.txt
[2026-10-01 18:38:12] RENAMED  logs/new.txt -> logs/renamed.txt
[2026-10-01 18:38:12] DELETED  logs/renamed.txt
^CStopped.
```

`watch` stops cleanly on Ctrl+C (SIGINT) or SIGTERM. One write often raises more than one `CHANGED` event, as shown above; that is how `FileSystemWatcher` behaves. If the OS event buffer overflows, an `ERROR` line warns that events may have been missed. Run `verify` afterwards to get an authoritative result.

## Using the library

```csharp
using FileIntegrityMonitor.Core;

var checker = new IntegrityChecker("/srv/www");
Manifest baseline = checker.CreateBaseline();      // or Manifest.Load(path)
baseline.Save("/secure/www.manifest.json");

IntegrityReport report = checker.Verify(baseline);
if (!report.IsClean)
{
    // report.Added / report.Modified / report.Deleted are sorted relative paths
}

// Pure comparison of two path -> hash maps (no file system access):
IntegrityReport diff = IntegrityChecker.Compare(baselineHashes, currentHashes);
```
