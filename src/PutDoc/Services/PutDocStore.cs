using System.Text.Json;
using System.Text.Json.Serialization;

namespace PutDoc.Services;

public class PutDocStore : IPutDocStore
{
    public string RootPath { get; }
    public string PutDocFilePath => Path.Combine(RootPath, ".putDoc");

    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PutDocStore(IConfiguration cfg)
    {
        RootPath = cfg["PutDocRootPath"]
                   ?? cfg["ROOT"]              // from PUTDOC_ROOT
                   ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(RootPath);
    }

    public async Task<PutDocFile> LoadAsync()
    {
        if (!File.Exists(PutDocFilePath))
        {
            var rootLeaf = new Leaf { Title = "Index" };
            var firstPage = new Page { Title = "Welcome", Snippets = new() { new Snippet { Html = "<h2>Welcome to PutDoc</h2><p>Edit me in the HTML Editor.</p>" } } };

            var doc = new PutDocFile
            {
                RootLeafId = rootLeaf.Id,
                Leafs = { [rootLeaf.Id] = rootLeaf },
                Pages = { [firstPage.Id] = firstPage }
            };
            rootLeaf.PageIds.Add(firstPage.Id);
            await SaveAsync(doc);
            return doc;
        }

        await using var fs = File.OpenRead(PutDocFilePath);
        var loaded = await JsonSerializer.DeserializeAsync<PutDocFile>(fs, _json);
        return loaded ?? new PutDocFile();
    }

    public async Task SaveAsync(PutDocFile file)
    {
        await using var fs = File.Create(PutDocFilePath);
        await JsonSerializer.SerializeAsync(fs, file, _json);
    }
}
