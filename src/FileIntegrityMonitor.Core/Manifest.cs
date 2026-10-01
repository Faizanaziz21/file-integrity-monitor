using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileIntegrityMonitor.Core;

/// <summary>A baseline snapshot: relative file path -> SHA-256 hex digest.</summary>
public sealed class Manifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Algorithm { get; init; } = "SHA-256";

    public string Root { get; init; } = "";

    public DateTimeOffset CreatedUtc { get; init; }

    [JsonConverter(typeof(SortedOrdinalDictionaryConverter))]
    public SortedDictionary<string, string> Files { get; init; } = new(StringComparer.Ordinal);

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static Manifest Load(string path)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Manifest '{path}' is empty.");
        if (!string.Equals(manifest.Algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported manifest algorithm '{manifest.Algorithm}'.");
        }

        return manifest;
    }

    /// <summary>Keeps the deserialized dictionary ordinal-sorted so output order is stable.</summary>
    private sealed class SortedOrdinalDictionaryConverter : JsonConverter<SortedDictionary<string, string>>
    {
        public override SortedDictionary<string, string> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader)
                ?? new Dictionary<string, string>();
            return new SortedDictionary<string, string>(raw, StringComparer.Ordinal);
        }

        public override void Write(
            Utf8JsonWriter writer, SortedDictionary<string, string> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, hash) in value)
            {
                writer.WriteString(key, hash);
            }

            writer.WriteEndObject();
        }
    }
}
