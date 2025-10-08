// Services/DocCatalogService.cs
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PutDoc.Services;

public interface IDocCatalogService
{
    Task<IReadOnlyList<DocMeta>> ListAsync(CancellationToken ct = default);
    Task<DocMeta?> GetAsync(Guid id, CancellationToken ct = default);

    Task<Guid> CreateAsync(string name, string? initialJson = null, CancellationToken ct = default);
    Task<bool> RenameAsync(Guid id, string newName, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<(string json, int version)?> LoadDocumentAsync(Guid id, CancellationToken ct = default);
    Task SaveDocumentAsync(Guid id, string json, int? expectedVersion = null, CancellationToken ct = default);

    Task<Guid> EnsureDefaultAsync(string defaultName = "Untitled", CancellationToken ct = default);

    Task<(string FileName, byte[] Bytes, string ContentType)> ExportRawJsonAsync(Guid id,
        CancellationToken ct = default);

    Task<(string FileName, byte[] Bytes, string ContentType)> ExportPackageAsync(Guid id,
        CancellationToken ct = default);
    
}

public sealed class DocCatalogService : IDocCatalogService
{
    private readonly string _baseDir;
    private readonly string _catalogPath;
    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _gate = new(1, 1); // protects catalog + version changes
    private List<DocMeta>? _catalog;                  // in-memory cache

    // optional per-doc gates if you want finer granularity later
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _docGates = new();

    public DocCatalogService(IConfiguration cfg, IHostEnvironment env)
    {
        _baseDir = cfg["PutDocRootPath"]
                           ?? cfg["ROOT"]              // from PUTDOC_ROOT
                           ?? Directory.GetCurrentDirectory();
        
        //_baseDir = Path.Combine(env.ContentRootPath, "App_Data", "PutDoc");
        Directory.CreateDirectory(_baseDir);
        _catalogPath = Path.Combine(_baseDir, "catalog.json");

        _json = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<IReadOnlyList<DocMeta>> ListAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        // return newest first
        return _catalog!.OrderByDescending(d => d.Modified).ToArray();
    }

    public async Task<DocMeta?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _catalog!.FirstOrDefault(d => d.Id == id);
    }

    public async Task<Guid> CreateAsync(string name, string? initialJson = null, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedNoLockAsync(ct);

            var meta = new DocMeta { Id = id, Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim(), Modified = now, Version = 0 };
            _catalog!.Add(meta);
            await PersistCatalogNoLockAsync(ct);

            // write content
            var contentPath = GetDocPath(id);
            var content = initialJson ?? "{}"; // keep agnostic — PutDocState knows how to initialize
            await AtomicWriteAsync(contentPath, content, ct);
        }
        finally { _gate.Release(); }

        return id;
    }

    public async Task<bool> RenameAsync(Guid id, string newName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedNoLockAsync(ct);
            var meta = _catalog!.FirstOrDefault(d => d.Id == id);
            if (meta is null) return false;

            meta.Name = string.IsNullOrWhiteSpace(newName) ? meta.Name : newName.Trim();
            meta.Modified = DateTimeOffset.UtcNow;
            await PersistCatalogNoLockAsync(ct);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedNoLockAsync(ct);
            var meta = _catalog!.FirstOrDefault(d => d.Id == id);
            if (meta is null) return false;

            _catalog!.Remove(meta);
            await PersistCatalogNoLockAsync(ct);

            var path = GetDocPath(id);
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<(string json, int version)?> LoadDocumentAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        var meta = _catalog!.FirstOrDefault(d => d.Id == id);
        if (meta is null) return null;

        var path = GetDocPath(id);
        if (!File.Exists(path)) return ( "{}", meta.Version ); // tolerate missing file

        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sr = new StreamReader(fs);
        var json = await sr.ReadToEndAsync();
        return (json, meta.Version);
    }

    public async Task SaveDocumentAsync(Guid id, string json, int? expectedVersion = null, CancellationToken ct = default)
    {
        // serialize writes + version bump behind catalog gate
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedNoLockAsync(ct);
            var meta = _catalog!.FirstOrDefault(d => d.Id == id)
                       ?? throw new FileNotFoundException($"Doc {id} not found");

            if (expectedVersion is int ev && ev != meta.Version)
                throw new ConcurrencyException($"Version mismatch. Expected {ev}, current {meta.Version}.");

            var path = GetDocPath(id);
            await AtomicWriteAsync(path, json ?? "{}", ct);

            meta.Version++;
            meta.Modified = DateTimeOffset.UtcNow;
            await PersistCatalogNoLockAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<Guid> EnsureDefaultAsync(string defaultName = "Untitled", CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct); 
        if (_catalog!.Count > 0) return _catalog[0].Id;
        
        var rootCollection = new Collection { Title = "Index" };
        var firstPage = new Page { Title = "Welcome", Snippets = new() { new Snippet { Html = "<h2>Welcome to PutDoc</h2><p>Edit me in the HTML Editor.</p>" } } };

        var doc = new PutDocFile
        {
            RootCollectionId = rootCollection.Id,
            Collections = { [rootCollection.Id] = rootCollection },
            Pages = { [firstPage.Id] = firstPage }
        };
        rootCollection.PageIds.Add(firstPage.Id);

        return await CreateAsync(defaultName, initialJson: JsonSerializer.Serialize(doc), ct);
    }

    // ===== helpers =====

    private string GetDocPath(Guid id) => Path.Combine(_baseDir, $"{id:N}.putdoc.json");

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_catalog is not null) return;
        await _gate.WaitAsync(ct);
        try { await EnsureLoadedNoLockAsync(ct); }
        finally { _gate.Release(); }
    }

    private async Task EnsureLoadedNoLockAsync(CancellationToken ct)
    {
        if (_catalog is not null) return;

        if (!File.Exists(_catalogPath))
        {
            _catalog = new List<DocMeta>();
            await PersistCatalogNoLockAsync(ct);
            return;
        }

        using var fs = File.Open(_catalogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _catalog = (await JsonSerializer.DeserializeAsync<List<DocMeta>>(fs, _json, ct)) ?? new List<DocMeta>();
    }

    private async Task PersistCatalogNoLockAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
        var tmp = _catalogPath + ".tmp";

        await using (var fs = File.Open(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(fs, _catalog, _json, ct);
        }

        // Atomic-ish replace, cross-platform
        if (File.Exists(_catalogPath))
        {
            try { File.Replace(tmp, _catalogPath, null); }
            catch { File.Move(tmp, _catalogPath, overwrite: true); }
        }
        else
        {
            File.Move(tmp, _catalogPath, overwrite: true);
        }
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";

        await File.WriteAllTextAsync(tmp, content, ct);

        if (File.Exists(path))
        {
            try { File.Replace(tmp, path, null); }
            catch { File.Move(tmp, path, overwrite: true); }
        }
        else
        {
            File.Move(tmp, path, overwrite: true);
        }
    }
    
    // Services/DocCatalogService.Export.cs  (or inside the same class)

    public async Task<(string FileName, byte[] Bytes, string ContentType)> ExportRawJsonAsync(Guid id, CancellationToken ct = default)
    {
        var meta = await GetAsync(id, ct) ?? throw new FileNotFoundException($"Doc {id} not found");
        var (json, _) = await LoadDocumentAsync(id, ct) ?? ("{}", meta.Version);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var name = SanitizeFileName(meta.Name);
        return ($"{name}.putdoc.json", bytes, "application/json; charset=utf-8");
    }

    public async Task<(string FileName, byte[] Bytes, string ContentType)> ExportPackageAsync(Guid id, CancellationToken ct = default)
    {
        var meta = await GetAsync(id, ct) ?? throw new FileNotFoundException($"Doc {id} not found");
        var (json, ver) = await LoadDocumentAsync(id, ct) ?? ("{}", meta.Version);
        var pkg = new
        {
            meta = new { meta.Id, meta.Name, Version = ver, meta.Modified },
            content = JsonDocument.Parse(json).RootElement
        };
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(pkg, new JsonSerializerOptions { WriteIndented = true });
        var name = SanitizeFileName(meta.Name);
        return ($"{name}.putdoc.pkg.json", bytes, "application/json; charset=utf-8");
    }

    private static string SanitizeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Document" : safe.Trim();
    }

}
