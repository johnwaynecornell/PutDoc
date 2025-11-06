using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Frozen;

// ---------- Options ----------
static class PutDocJson
{
    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

// ---------- DTOs for “complete” (expanded) JSON ----------
public record PageExportDto(Guid Id, string Title, List<SnippetDto> Snippets);
public record SnippetDto(Guid Id, string Html);

public record CollectionExportDto(
    Guid Id,
    string Title,
    List<CollectionExportDto> Collections, // expanded children
    List<PageExportDto> Pages              // expanded pages
);

// ---------- Export Utilities ----------
public static class PutDocExport
{
    // 0) Raw serialize (by-reference) — direct copy of your objects (IDs remain)
    public static string ExportRaw(PutDocFile file)
        => JsonSerializer.Serialize(file, PutDocJson.Pretty);

    public static string ExportRaw(Page page)
        => JsonSerializer.Serialize(page, PutDocJson.Pretty);

    public static string ExportRaw(Collection collection)
        => JsonSerializer.Serialize(collection, PutDocJson.Pretty);

    // 1) Page (complete) — page with its snippets expanded
    public static PageExportDto PageToDto(Page page)
        => new(
            page.Id,
            page.Title,
            page.Snippets.Select(s => new SnippetDto(s.Id, s.Html)).ToList()
        );

    public static string ExportPageComplete(Page page)
        => JsonSerializer.Serialize(PageToDto(page), PutDocJson.Pretty);

    // 2) Collection (deep) — recursively expand children and include full pages
    public static CollectionExportDto CollectionToDtoDeep(PutDocFile file, Guid collectionId)
    {
        if (!file.Collections.TryGetValue(collectionId, out var coll))
            throw new KeyNotFoundException($"Collection {collectionId} not found.");

        var childDtos = coll.ChildCollectionIds
            .Select(id => CollectionToDtoDeep(file, id))
            .ToList();

        var pageDtos = coll.PageIds
            .Select(pid =>
            {
                if (!file.Pages.TryGetValue(pid, out var p))
                    throw new KeyNotFoundException($"Page {pid} not found (referenced by collection {coll.Id}).");
                return PageToDto(p);
            })
            .ToList();

        return new CollectionExportDto(
            coll.Id,
            coll.Title,
            childDtos,
            pageDtos
        );
    }

    public static string ExportCollectionDeep(PutDocFile file, Guid collectionId)
        => JsonSerializer.Serialize(CollectionToDtoDeep(file, collectionId), PutDocJson.Pretty);

    // 3) Whole-file (deep) — start at root and expand everything (nice for backups/paste)
    public static string ExportWholeFileDeep(PutDocFile file)
    {
        var root = CollectionToDtoDeep(file, file.RootCollectionId);
        var wrapper = new
        {
            name = file.Name,
            rootCollection = root
        };
        return JsonSerializer.Serialize(wrapper, PutDocJson.Pretty);
    }
}

#region Import Utilities

public static class PutDocImport
{
    // ---------- Raw imports (by-reference) ----------
    public static PutDocFile ImportRawFile(string json)
        => JsonSerializer.Deserialize<PutDocFile>(json, PutDocJson.Pretty)
           ?? throw new InvalidDataException("Invalid PutDocFile JSON");

    public static Page ImportRawPage(string json)
        => JsonSerializer.Deserialize<Page>(json, PutDocJson.Pretty)
           ?? throw new InvalidDataException("Invalid Page JSON");

    public static Collection ImportRawCollection(string json)
        => JsonSerializer.Deserialize<Collection>(json, PutDocJson.Pretty)
           ?? throw new InvalidDataException("Invalid Collection JSON");

    // ---------- DTO imports (complete/deep) ----------

    // 1) Page (complete): creates or updates a Page inside 'file'.
    // overwrite=false => upsert (keeps existing Title/Snippets if present, unions snippets by Id)
    // overwrite=true  => replace Title/Snippets with imported exactly
    public static Page ImportPageComplete(PutDocFile file, string pageJson, bool overwrite = false)
    {
        var dto = JsonSerializer.Deserialize<PageExportDto>(pageJson, PutDocJson.Pretty)
                  ?? throw new InvalidDataException("Invalid PageExportDto JSON");
        
        return ImportPageComplete(file, dto, overwrite);
    }

    public static Page ImportPageComplete(PutDocFile file, PageExportDto dto, bool overwrite = false, bool newGuid=false)
    {
        var dtoId = newGuid ? Guid.NewGuid() : dto.Id;
        
        if (!file.Pages.TryGetValue(dtoId, out var page))
        {
            page = new Page { Id = dtoId, Title = dto.Title, Snippets = [] };
            file.Pages[page.Id] = page;
        }
        if (overwrite)
        {
            page.Title = dto.Title;
            page.Snippets = dto.Snippets
                .Select(s => new Snippet { Id = newGuid ? Guid.NewGuid() : s.Id, Html = s.Html })
                .ToList();
        }
        else
        {
            // Upsert title if original looks default
            if (string.Equals(page.Title, "Page", StringComparison.Ordinal))
                page.Title = dto.Title;

            var existing = page.Snippets.ToDictionary(s => s.Id);
            foreach (var s in dto.Snippets)
            {
                if (existing.TryGetValue(s.Id, out var found))
                {
                    // Non-destructive: only update Html if different and found looks default
                    if (string.Equals(found.Html, "<p>New snippet</p>", StringComparison.Ordinal))
                        found.Html = s.Html;
                }
                else
                {
                    page.Snippets.Add(new Snippet { Id = newGuid ? Guid.NewGuid() : s.Id, Html = s.Html });
                }
            }
        }
        return page;
    }

    // 2) Collection (deep): recursively imports a collection tree with full pages.
    // attachTo = optional parent collection to attach imported root as a child (adds link if not present)
    // overwrite=false => non-destructive merge (unions children/pages)
    // overwrite=true  => replace structure of any existing collections with imported shape exactly
    public static Collection ImportCollectionDeep(
        PutDocFile file,
        string collectionJson,
        Guid? attachTo = null,
        bool overwrite = false, bool newGuid = false)
    {
        var dto = JsonSerializer.Deserialize<CollectionExportDto>(collectionJson, PutDocJson.Pretty)
                  ?? throw new InvalidDataException("Invalid CollectionExportDto JSON");
        var importedRoot = ImportCollectionDeep(file, dto, overwrite, new HashSet<Guid>(), newGuid);

        if (attachTo.HasValue)
        {
            if (!file.Collections.TryGetValue(attachTo.Value, out var parent))
                throw new KeyNotFoundException($"Parent collection {attachTo} not found.");
            if (!parent.ChildCollectionIds.Contains(importedRoot.Id))
                parent.ChildCollectionIds.Add(importedRoot.Id);
        }
        else if (file.RootCollectionId == Guid.Empty)
        {
            file.RootCollectionId = importedRoot.Id;
        }
        return importedRoot;
    }

    private static Collection ImportCollectionDeep(
        PutDocFile file,
        CollectionExportDto dto,
        bool overwrite,
        HashSet<Guid> visited, bool newGuid = false)
    {
        Guid dtoId = newGuid ? Guid.NewGuid() : dto.Id;
        
        if (!visited.Add(dtoId))
            throw new InvalidOperationException($"Cycle detected while importing collection {dtoId}.");

        if (!file.Collections.TryGetValue(dtoId, out var coll))
        {
            coll = new Collection { Id = dtoId, Title = dto.Title };
            file.Collections[coll.Id] = coll;
        }

        if (overwrite)
        {
            coll.Title = dto.Title;
            coll.ChildCollectionIds.Clear();
            coll.PageIds.Clear();
        }
        else
        {
            if (string.Equals(coll.Title, "Collection", StringComparison.Ordinal))
                coll.Title = dto.Title;
        }

        // Import pages first, collect their ids
        foreach (var p in dto.Pages)
        {
            var page = ImportPageComplete(file, p, overwrite, newGuid);
            if (!coll.PageIds.Contains(page.Id))
                coll.PageIds.Add(page.Id);
        }

        // Recurse children
        foreach (var child in dto.Collections)
        {
            var childColl = ImportCollectionDeep(file, child, overwrite, visited, newGuid);
            if (!coll.ChildCollectionIds.Contains(childColl.Id))
                coll.ChildCollectionIds.Add(childColl.Id);
        }

        visited.Remove(dtoId);
        return coll;
    }

    // 3) Whole-file (deep): { name, rootCollection } wrapper
    // overwrite=false => non-destructive merge; sets RootCollectionId if empty
    // overwrite=true  => clears existing collections/pages first, then imports
    public static PutDocFile ImportWholeFileDeep(PutDocFile target, string json, bool overwrite = false)
    {
        var wrapper = JsonSerializer.Deserialize<WholeDeepWrapper>(json, PutDocJson.Pretty)
                      ?? throw new InvalidDataException("Invalid WholeFileDeep JSON");

        if (overwrite)
        {
            target.Collections.Clear();
            target.Pages.Clear();
            target.RootCollectionId = Guid.Empty;
        }

        target.Name = wrapper.name ?? target.Name;
        var root = ImportCollectionDeep(target, wrapper.rootCollection, overwrite, new HashSet<Guid>());
        if (target.RootCollectionId == Guid.Empty)
            target.RootCollectionId = root.Id;

        return target;
    }

    private sealed record WholeDeepWrapper(string? name, CollectionExportDto rootCollection);
}
#endregion

public enum ExportPayloadKind
{
    Unknown,
    Page,           // PageExportDto
    Collection,     // CollectionExportDto (includes wrapper.rootCollection)
}

public sealed record ParsedExport(
    ExportPayloadKind Kind,
    PageExportDto? Page,
    CollectionExportDto? Collection
);

public static class PutDocDetect
{
    public static ParsedExport ParsePageOrCollection(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            return new ParsedExport(ExportPayloadKind.Unknown, null, null);
        }

        var root = doc.RootElement;

        // 1) Whole-file deep wrapper: { "name": "...", "rootCollection": { ... } }
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rootCollection", out var rootCollection))
        {
            var collection = JsonSerializer.Deserialize<CollectionExportDto>(rootCollection.GetRawText(), PutDocJson.Pretty)
                             ?? throw new InvalidDataException("Invalid rootCollection payload.");
            return new ParsedExport(ExportPayloadKind.Collection, null, collection);
        }

        // 2) Heuristics by shape (cheap & reliable):
        // PageExportDto  -> { id, title, snippets: [...] }
        // CollectionDto  -> { id, title, collections: [...], pages: [...] }
        bool hasSnippets   = root.TryGetProperty("snippets", out var _);
        bool hasCollections= root.TryGetProperty("collections", out var _);
        bool hasPages      = root.TryGetProperty("pages", out var _);

        if (hasSnippets && !hasCollections && !hasPages)
        {
            var page = JsonSerializer.Deserialize<PageExportDto>(json, PutDocJson.Pretty)
                       ?? throw new InvalidDataException("Invalid Page export.");
            return new ParsedExport(ExportPayloadKind.Page, page, null);
        }

        if (hasCollections && hasPages)
        {
            var collection = JsonSerializer.Deserialize<CollectionExportDto>(json, PutDocJson.Pretty)
                             ?? throw new InvalidDataException("Invalid Collection export.");
            return new ParsedExport(ExportPayloadKind.Collection, null, collection);
        }

        // 3) Fallback: try Page then Collection (covers edge cases)
        try
        {
            var page = JsonSerializer.Deserialize<PageExportDto>(json, PutDocJson.Pretty);
            if (page is not null)
                return new ParsedExport(ExportPayloadKind.Page, page, null);
        }
        catch { /* ignore and try collection */ }

        try
        {
            var collection = JsonSerializer.Deserialize<CollectionExportDto>(json, PutDocJson.Pretty);
            if (collection is not null)
                return new ParsedExport(ExportPayloadKind.Collection, null, collection);
        }
        catch { /* final fallthrough */ }

        return new ParsedExport(ExportPayloadKind.Unknown, null, null);
    }
}



// ---------- Example usage ----------
/*
var file = new PutDocFile { Name = "PutDoc", RootCollectionId = rootId };
// ...populate file.Collections and file.Pages...

// A) Copy/paste of a single Page (complete):
var pageJson = PutDocExport.ExportPageComplete(file.Pages[somePageId]);

// B) Copy/paste of a Collection subtree (deep, with pages/snippets expanded):
var subtreeJson = PutDocExport.ExportCollectionDeep(file, someCollectionId);

// C) Copy/paste of the entire document as a fully expanded tree:
var fullJson = PutDocExport.ExportWholeFileDeep(file);

// D) If you ever need the original, by-reference structures:
var rawFile = PutDocExport.ExportRaw(file);
*/

