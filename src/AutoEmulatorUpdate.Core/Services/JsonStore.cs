using System.Text.Json;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<T> LoadAsync<T>(string path, T fallback, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return fallback;
        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(fs, Options, ct) ?? fallback;
        }
        catch { return fallback; }
    }

    public async Task SaveAsync<T>(string path, T value, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, value, Options, ct);
        File.Move(tmp, path, true);
    }

    public async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(value, Options);
        await File.AppendAllTextAsync(path, line + Environment.NewLine, ct);
    }

    public IEnumerable<T> ReadJsonLines<T>(string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            T? item = default;
            try { item = JsonSerializer.Deserialize<T>(line, Options); } catch { }
            if (item is not null) yield return item;
        }
    }
}
