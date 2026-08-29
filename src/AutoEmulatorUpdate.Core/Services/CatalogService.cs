using System.Text.Json;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class CatalogService(AppPaths paths)
{
    private readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<EmulatorDefinition>> LoadAsync(string bundledRoot, CancellationToken ct = default)
    {
        var map = new Dictionary<string, EmulatorDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { bundledRoot, paths.UserManifestRoot })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    await using var fs = File.OpenRead(file);
                    var def = await JsonSerializer.DeserializeAsync<EmulatorDefinition>(fs, _options, ct);
                    if (def is not null && !string.IsNullOrWhiteSpace(def.Id))
                        map[def.Id] = def;
                }
                catch { }
            }
        }
        return map.Values.OrderBy(x => x.Name).ToArray();
    }

    public async Task UpdateRemoteCatalogAsync(Uri manifestUri, HttpClient http, CancellationToken ct = default)
    {
        var json = await http.GetStringAsync(manifestUri, ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Catalog must be a JSON array.");

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var def = element.Deserialize<EmulatorDefinition>(_options);
            if (def is null || string.IsNullOrWhiteSpace(def.Id)) continue;
            var file = Path.Combine(paths.UserManifestRoot, def.Id + ".json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true }), ct);
        }
    }
}
