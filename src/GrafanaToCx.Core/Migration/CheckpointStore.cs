using Newtonsoft.Json;

namespace GrafanaToCx.Core.Migration;

public sealed class CheckpointStore
{
    private readonly string _filePath;
    private Dictionary<string, CheckpointEntry> _entries = new();

    public CheckpointStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return;

        var json = await File.ReadAllTextAsync(_filePath, ct);
        _entries = JsonConvert.DeserializeObject<Dictionary<string, CheckpointEntry>>(json)
                   ?? new Dictionary<string, CheckpointEntry>();
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        var json = JsonConvert.SerializeObject(_entries, Formatting.Indented);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    public CheckpointEntry? Get(string grafanaUid) =>
        _entries.TryGetValue(grafanaUid, out var entry) ? entry : null;

    public void Upsert(CheckpointEntry entry) =>
        _entries[entry.GrafanaUid] = entry;

    public IReadOnlyCollection<CheckpointEntry> All => _entries.Values;
}
