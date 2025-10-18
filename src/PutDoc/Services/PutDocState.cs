// PutDoc.Services/PutDocState.cs
// PutDoc.Services/PutDocState.cs

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace PutDoc.Services;

public class PutDocState
{
    public PutDocFile Doc { get; private set; } = new(); // in-memory content
    public DocMeta? Meta { get; private set; } // catalog entry (id, name, version, modified)

    public Guid CurrentDocId => Meta?.Id ?? Guid.Empty;
    public int DocVersion => Meta?.Version ?? 0; // for CAS
    public string DocName => Meta?.Name ?? "Untitled";

    public Guid? SelectedPageId { get; set; }
    public Guid? SelectedSnippetId { get; set; }

    public string CurrentUserId { get; set; } = new Guid().ToString();

    //readonly IPutDocStore _store;
    readonly IAngleSoftFilter _filter;
    private readonly IDocCatalogService _catalog;
    private readonly IJSRuntime _js; // if you need it for RO attribute

    public PutDocState(IDocCatalogService catalog, IAngleSoftFilter filter, IJSRuntime js)
    {
        _catalog = catalog;
        _filter = filter;
        _js = js;
    }

    public bool IsReadOnly { get; private set; } = true;

    public void SetReadOnly(bool ro)
    {
        if (IsReadOnly != ro)
        {
            IsReadOnly = ro;
            _ = _js.InvokeVoidAsync(
                "document.documentElement.setAttribute", "data-pd-readonly", ro ? "true" : "false");
            IsReadOnly = ro;
            Changed?.Invoke();
        }
    }

    public bool IsDirty { get; private set; }

    public void MarkDirty()
    {
        if (IsDirty) return;
        IsDirty = true;
        Changed?.Invoke();
    }

    public void ClearDirty()
    {
        if (!IsDirty) return;
        IsDirty = false;
        Changed?.Invoke();
    }


    // NEW: change event
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
    public event Action<string>? CheckpointRequested;

    // helper you can call from anywhere (ToolbarHub, etc.)
    public void RequestCheckpoint(string label) => CheckpointRequested?.Invoke(label);

    public enum ContextChangeDecision
    {
        Proceed,
        Cancel,
        Save,
        Discard
    }

// Raised when someone wants to change context (page/snippet) but we may be Frozen/Dirty.
// Exactly ONE listener (the active HtmlEditor) should handle and return a decision.
    public event Func<Task<ContextChangeDecision>>? ContextChangeRequested;

// Fire-and-forget signal the HtmlEditor should perform a SaveNow
    public event Action? SaveRequested;

    // Let State ask the editor whether it’s frozen.
    public Func<bool> IsFrozen { get; set; } // HtmlEditor will assign}
    public Action ClearFrozen { get; set; } // HtmlEditor will assign}

// Internal flag to bypass the guard for system flows.
    private int _suppressGuardDepth = 0;

    public IDisposable SuppressGuardScope()
    {
        _suppressGuardDepth++;
        return new Scope(() => _suppressGuardDepth--);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Action _on;
        public Scope(Action on) => _on = on;
        public void Dispose() => _on();
    }

    public async Task<bool> EnsureClearForTextChangeInternalAsync()
    {
        if (_suppressGuardDepth > 0) return true; // internal/system: skip guard

        if (!(IsDirty || (IsFrozen?.Invoke() ?? false))) return true;

        if (ContextChangeRequested is null) return false; // no UI to ask → block

        var decision = (!IsDirty) ? ContextChangeDecision.Discard : await ContextChangeRequested.Invoke();

        switch (decision)
        {
            case ContextChangeDecision.Cancel:
                return false;

            case ContextChangeDecision.Discard:
                return true;

            case ContextChangeDecision.Save:
                // If editor is frozen, treat like cancel (your modal already disables Save in that case).
                return !IsDirty; // only proceed if save cleared dirty
            case ContextChangeDecision.Proceed:
            default:
                return true;
        }
    }

    public async Task<bool> TryLoadDocumentAsync(Guid docId)
    {
        if (!await EnsureClearForTextChangeInternalAsync()) return false;
        await LoadAsync(docId);
        return true;
    }

    public async Task<bool> TrySelectPageAsync(Guid pageId)
    {
        if (!await EnsureClearForTextChangeInternalAsync()) return false;
        SelectPage(pageId);
        return true;
    }

    public async Task<bool> TrySelectSnippetAsync(Guid snippetId)
    {
        if (!await EnsureClearForTextChangeInternalAsync()) return false;
        SelectSnippet(snippetId);
        return true;
    }
/*
// Examples of “core” versions that don’t trigger prompts:
    private async Task LoadDocumentCoreAsync(Guid docId)
    {
        using (SuppressGuardScope()) { this.LoadAsync(docId) // load file, set Doc, etc.
        ClearDirty(); }

    }*/

    public async Task<bool> RenameAsync(string newName)
    {
        if (Meta is null) return false;
        var ok = await _catalog.RenameAsync(Meta.Id, newName);
        if (ok)
        {
            Meta = Meta with { Name = newName, Modified = DateTimeOffset.UtcNow };
            Changed?.Invoke();
        }

        return ok;
    }


    // NEW: select snippet + notify others to re-render
    public void SelectSnippet(Guid? id)
    {
        CancelSelectionEdit();
        SelectedSnippetId = id;
        Notify();
    }

    public void SelectPage(Guid id)
    {
        CancelSelectionEdit();
        SelectedPageId = id;
        SelectedSnippetId = CurrentPage()?.Snippets.FirstOrDefault()?.Id;
        Notify();
    }

    /*
    public async Task LoadAsync()
    {
        Doc = await _store.LoadAsync();

        // Ensure every snippet has puids (server is source of truth)
        foreach (var p in Doc.Pages.Values)
            for (int i = 0; i < p.Snippets.Count; i++)
                p.Snippets[i].Html = await HtmlPuid.EnsurePuidsAsync(p.Snippets[i].Html ?? "");

        SelectedPageId ??= Doc.Pages.Keys.FirstOrDefault();
        SelectedSnippetId ??= CurrentPage()?.Snippets.FirstOrDefault()?.Id;
        Notify();
    }
    */
    public static Stream ConvertStringToStream(string inputString, Encoding encoding = null)
    {
        // Use UTF8 encoding by default if no encoding is specified
        encoding ??= Encoding.UTF8;

        // Convert the string to a byte array using the specified encoding
        byte[] byteArray = encoding.GetBytes(inputString);

        // Create a MemoryStream from the byte array
        MemoryStream memoryStream = new MemoryStream(byteArray);

        return memoryStream;
    }

    public async Task<Guid> GetDefaultDocIdAsync()
    {
        // Whatever your catalog logic is
        return await _catalog.EnsureDefaultAsync("Untitled");
    }

    // PutDocState.cs
    private Task? _inFlightLoad;
    private Guid? _inFlightDocId;

    public Task EnsureLoadedAsync(Guid id)
    {
        // If already loaded, nothing to do
        if (Meta?.Id == id && Doc is not null) return Task.CompletedTask;

        Console.WriteLine($"[state] ensure {id} inFlightId={_inFlightDocId} hasTask={_inFlightLoad is not null}");

        // If a load for this id is already running, await it
        if (_inFlightLoad is not null && _inFlightDocId == id)
            return _inFlightLoad;

        // Start a new load and remember it
        _inFlightDocId = id;
        var task = LoadAsync(id);
        _inFlightLoad = task.ContinueWith(t =>
        {
            // Clear only if we’re still the same task
            if (ReferenceEquals(_inFlightLoad, task))
            {
                _inFlightLoad = null;
                _inFlightDocId = null;
            }
        }, TaskScheduler.Default);

        return task;
    }


// Keep LoadDefaultAsync if other callers need it, but avoid using it in routing.
    public async Task<Guid> LoadDefaultAsync()
    {
        var id = await GetDefaultDocIdAsync();
        await LoadAsync(id);
        return id;
    }
/*
    public async Task<Guid> LoadDefaultAsync()
    {
        var id = await _catalog.EnsureDefaultAsync("Untitled");
        await LoadAsync(id);
        return id;
    } */

    
    // PutDocState.cs
    private long _loadEpoch = 0; // increments per load request
    private long _appliedEpoch = 0; // last epoch that successfully applied
    private CancellationTokenSource? _loadCts;

    public enum RecoveryStrategy
    {
        RetainIds,
        RekeyNewIds
    }

    public static class PutDocNormalize
    {
        public static void NormalizeDocument(PutDocFile doc,
            RecoveryStrategy strategy = RecoveryStrategy.RetainIds,
            string recoveredSuffix = " (recovered)")
        {
            if (doc is null) return;

            // Ensure root exists (no reorder)
            if (!doc.Collections.ContainsKey(doc.RootCollectionId) && doc.Collections.Count > 0)
                doc.RootCollectionId = doc.Collections.Keys.First();

            // Helper: make a title for recovered nodes
            static string NameFor(string kind, Guid id, string suf)
                => $"{kind} {id.ToString()[..8]}{suf}";

            // Fix each collection’s lists in place (order preserved)
            foreach (var c in doc.Collections.Values)
            {
                // --- PageIds: drop dupes (keep first), recover missing ---
                if (c.PageIds is { Count: > 0 })
                {
                    var seen = new HashSet<Guid>();
                    for (int i = 0; i < c.PageIds.Count; i++)
                    {
                        var pid = c.PageIds[i];

                        // de-dupe (preserve first occurrence)
                        if (!seen.Add(pid))
                        {
                            c.PageIds.RemoveAt(i);
                            i--;
                            continue;
                        }

                        // recover missing page
                        if (!doc.Pages.ContainsKey(pid))
                        {
                            var newId = (strategy == RecoveryStrategy.RetainIds) ? pid : Guid.NewGuid();

                            if (strategy == RecoveryStrategy.RekeyNewIds)
                                c.PageIds[i] = newId; // keep index, swap id

                            // create stub page only once
                            if (!doc.Pages.ContainsKey(newId))
                            {
                                doc.Pages[newId] = new Page
                                {
                                    Id = newId,
                                    Title = NameFor("Page", newId, recoveredSuffix),
                                    Snippets = new List<Snippet>() // empty, valid page
                                };
                            }
                        }
                    }
                }

                // --- ChildCollectionIds: drop dupes, recover missing ---
                if (c.ChildCollectionIds is { Count: > 0 })
                {
                    var seen = new HashSet<Guid>();
                    for (int i = 0; i < c.ChildCollectionIds.Count; i++)
                    {
                        var cid = c.ChildCollectionIds[i];

                        if (!seen.Add(cid))
                        {
                            c.ChildCollectionIds.RemoveAt(i);
                            i--;
                            continue;
                        }

                        if (!doc.Collections.ContainsKey(cid))
                        {
                            var newId = (strategy == RecoveryStrategy.RetainIds) ? cid : Guid.NewGuid();

                            if (strategy == RecoveryStrategy.RekeyNewIds)
                                c.ChildCollectionIds[i] = newId;

                            if (!doc.Collections.ContainsKey(newId))
                            {
                                doc.Collections[newId] = new Collection
                                {
                                    Id = newId,
                                    Title = NameFor("Collection", newId, recoveredSuffix),
                                    PageIds = new List<Guid>(),
                                    ChildCollectionIds = new List<Guid>()
                                };
                            }
                        }
                    }
                }
            }

            // Optional: handle orphan pages (present but not referenced anywhere)
            // Leave as-is if you allow loose pages; or place them under a dedicated
            // "Recovered" collection without touching existing indices.

            // NB: No sorting anywhere; all lists remain in authored order.
        }
    }

    private void LogDocAnomalies(PutDocFile doc)
    {
        foreach (var c in doc.Collections.Values)
        {
            if (c.PageIds is null) continue;
            var missing = c.PageIds.Where(pid => !doc.Pages.ContainsKey(pid)).ToList();
            if (missing.Count > 0)
                Console.WriteLine($"[validate] Collection {c.Title} has {missing.Count} missing PageIds");
        }
    }
    public async Task LoadAsync(Guid id)
    {

        // (Optional) move the epoch bump to the very start to be extra explicit
        Interlocked.Increment(ref _loadEpoch);
        var myEpoch = _loadEpoch;

        Console.WriteLine($"[State] Load start id={id} myEpoch={myEpoch} latest={Volatile.Read(ref _loadEpoch)}");


        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            var meta = await _catalog.GetAsync(id)
                       ?? throw new FileNotFoundException($"Doc {id} not found");

            // Load payload (use ct if your API allows)
            var payload = await _catalog.LoadDocumentAsync(id /*, ct */) ?? ("{}", meta.Version);
            if (ct.IsCancellationRequested || myEpoch != Volatile.Read(ref _loadEpoch))
            {
                Console.WriteLine($"[State] Load stale id={id} myEpoch={myEpoch} — discard");
                return;
            }

            // Discard stale finishes
            if (myEpoch != _loadEpoch) return;

            // Parse JSON
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            using var fs = ConvertStringToStream(payload.json);
            var loadedDoc = await JsonSerializer.DeserializeAsync<PutDocFile>(fs, jsonOptions)
                            ?? new PutDocFile();

            LogDocAnomalies(loadedDoc);

            PutDocNormalize.NormalizeDocument(loadedDoc);

            // Ensure puids in all snippets
            foreach (var p in loadedDoc.Pages.Values)
            {
                for (int i = 0; i < p.Snippets.Count; i++)
                {
                    p.Snippets[i].Html = await HtmlPuid.EnsurePuidsAsync(p.Snippets[i].Html ?? "");
                }
            }

            // ---- Apply atomically to state (this is the only mutation point) ----
            Doc = loadedDoc;
            Meta = meta with { Version = payload.version };

            // Reset selection for the new doc (don’t carry old ids forward)
            SelectedPageId = Doc.Collections.TryGetValue(Doc.RootCollectionId, out var root) &&
                             root.PageIds is { Count: > 0 } ? root.PageIds[0]
                : Doc.Pages.Keys.FirstOrDefault();

            SelectedSnippetId = (SelectedPageId is Guid pid && Doc.Pages.TryGetValue(pid, out var page) &&
                                 page.Snippets.Count > 0)
                ? page.Snippets[0].Id
                : null;
            //SelectedSnippetId = CurrentPage()?.Snippets.FirstOrDefault()?.Id;

            _appliedEpoch = myEpoch;

            // Mark clean and announce an EXTERNAL content change so listeners refresh
            LastUpdateSource = UpdateSource.External; // ← make sure this property exists as in your editor logic
            ContentVersion++; // ← bump the version so sync code sees a change
            Console.WriteLine($"[State] Load APPLY id={id} myEpoch={myEpoch} cv={ContentVersion}");
            IsDirty = false;
            Notify();


        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }


// Call this from wherever you persist the whole document
    public async Task SaveWholeDocumentAsync()
    {
        if (IsReadOnly) return;

        var id = CurrentDocId;
        if (id == Guid.Empty) throw new InvalidOperationException("No current document");

        var json = JsonSerializer.Serialize(Doc); // your existing serializer
        var expected = DocVersion; // CAS

        await _catalog.SaveDocumentAsync(id, json, expectedVersion: expected);

        // reflect bump locally
        Meta = Meta! with { Version = expected + 1, Modified = DateTimeOffset.UtcNow };
        ClearDirty();
        Changed?.Invoke();
    }

    public Page? CurrentPage() => SelectedPageId is Guid id && Doc.Pages.TryGetValue(id, out var p) ? p : null;
    public Snippet? CurrentSnippet() => CurrentPage()?.Snippets.FirstOrDefault(s => s.Id == SelectedSnippetId);

    public async Task SaveAsync()
    {
        await SaveWholeDocumentAsync();
    }

    public long ContentVersion { get; private set; }

    public enum UpdateSource
    {
        Editor,
        External
    }

    public UpdateSource LastUpdateSource { get; private set; } = UpdateSource.External;

    public async Task SetSnippetHtml(string html, bool isRawFromEditor = true)
    {
        var s = CurrentSnippet();
        if (s is null) return;

        // 1) sanitize editor text (AngleSoft)
        var cleaned = _filter.Filter(html);

        // 2) strip puids in case the editor text contained any (keeps editors clean)
        if (isRawFromEditor) cleaned = await HtmlPuid.StripPuidsAsync(cleaned);

        // 3) re-add puids for runtime reliability
        cleaned = await HtmlPuid.EnsurePuidsAsync(cleaned);

        s.Html = cleaned;

        LastUpdateSource = isRawFromEditor ? UpdateSource.Editor : UpdateSource.External;
        ContentVersion++;

        await SaveAsync(); // SaveAsync -> Notify()
    }

    public async Task AddSnippetBelow(Guid? afterId = null)
    {
        var page = CurrentPage();
        if (page is null) return;
        var idx = afterId is Guid a ? page.Snippets.FindIndex(x => x.Id == a) : page.Snippets.Count - 1;
        var insertAt = Math.Clamp(idx + 1, 0, page.Snippets.Count);
        page.Snippets.Insert(insertAt, new Snippet());
        await SaveAsync();
    }

    public async Task DeleteSnippet(Guid id)
    {
        var page = CurrentPage();
        if (page is null) return;
        var i = page.Snippets.FindIndex(s => s.Id == id);
        if (i >= 0) page.Snippets.RemoveAt(i);
        SelectedSnippetId = page.Snippets.ElementAtOrDefault(Math.Min(i, page.Snippets.Count - 1))?.Id;
        await SaveAsync();
    }

    public async Task CloneSnippet(Guid id)
    {
        var page = CurrentPage();
        if (page is null) return;
        var i = page.Snippets.FindIndex(s => s.Id == id);
        if (i < 0) return;
        var clone = page.Snippets[i] with { Id = Guid.NewGuid() };
        page.Snippets.Insert(i + 1, clone);
        await SaveAsync();
    }

    public async Task MoveSnippet(Guid id, int delta)
    {
        var page = CurrentPage();
        if (page is null) return;
        var i = page.Snippets.FindIndex(s => s.Id == id);
        if (i < 0) return;
        var j = Math.Clamp(i + delta, 0, page.Snippets.Count - 1);
        if (i == j) return;
        (page.Snippets[i], page.Snippets[j]) = (page.Snippets[j], page.Snippets[i]);
        await SaveAsync();
    }

    public enum FragmentScope
    {
        Inner,
        Outer
    }

    // 🔸 selection edit model
    public sealed class SelectionEdit
    {
        public bool IsActive { get; set; }
        public Guid SnippetId { get; set; }
        public string Selector { get; set; } = "";
        public string Html { get; set; } = ""; // fragment outerHTML

        public FragmentScope Scope { get; set; } = FragmentScope.Inner;
    }

    public SelectionEdit Selection { get; } = new(); // expose read-only object

    public void BeginSelectionEdit(Guid snippetId, string selector, string html)
    {
        Selection.IsActive = true;
        Selection.SnippetId = snippetId;
        Selection.Selector = selector;
        Selection.Html = html;
        SelectedSnippetId = snippetId; // make sure editor focuses this snippet
        Notify();
    }

    public async Task BeginFragmentEdit(Guid snippetId, string puid, FragmentScope scope = FragmentScope.Inner)
    {
        // close other selection if different
        if (Selection.IsActive && (Selection.SnippetId != snippetId || Selection.Selector != puid))
            CancelSelectionEdit();

        var page = CurrentPage();
        if (page is null) return;
        var snip = page.Snippets.FirstOrDefault(s => s.Id == snippetId);
        if (snip is null) return;

        var fragHtml = scope == FragmentScope.Outer
            ? await HtmlTransformService.ExtractFragmentOuterByPuidAsync(snip.Html ?? "", puid)
            : await HtmlTransformService.ExtractFragmentInnerByPuidAsync(snip.Html ?? "", puid);

        Selection.IsActive = true;
        Selection.SnippetId = snippetId;
        Selection.Selector = puid;
        Selection.Scope = scope;
        Selection.Html = await HtmlPuid.StripPuidsAsync(fragHtml);

        SelectedSnippetId = snippetId;
        ContentVersion++;
        Notify();
    }

    public async Task SetSelectionScope(FragmentScope scope)
    {
        if (!Selection.IsActive || Selection.SnippetId == Guid.Empty || string.IsNullOrEmpty(Selection.Selector))
            return;

        await BeginFragmentEdit(Selection.SnippetId, Selection.Selector!, scope);
    }


    // Services/PutDocState.cs
    public void CancelSelectionEdit()
    {
        Selection.IsActive = false;
        Selection.SnippetId = default;
        Selection.Selector = "";
        Selection.Html = "";
        Notify(); // triggers HtmlEditor to sync
    }

    // Create a new child Collection
    public async Task<Guid> CreateCollection(Guid parentCollectionId, string title = "New Collection")
    {
        var collection = new Collection { Title = title };
        Doc.Collections[collection.Id] = collection;
        if (Doc.Collections.TryGetValue(parentCollectionId, out var parent))
            parent.ChildCollectionIds.Add(collection.Id);
        await SaveAsync();
        Notify();
        return collection.Id;
    }

    // Create a new Page under a Collection
    public async Task<Guid> CreatePage(Guid collectionId, string title = "New Page")
    {
        var page = new Page { Title = title, Snippets = new() { new Snippet() } };
        Doc.Pages[page.Id] = page;
        if (Doc.Collections.TryGetValue(collectionId, out var collection))
            collection.PageIds.Add(page.Id);
        SelectedPageId = page.Id;
        SelectedSnippetId = page.Snippets.First().Id;
        await SaveAsync();
        Notify();
        return page.Id;
    }

    public async Task RenameCollection(Guid collectionId, string title)
    {
        if (Doc.Collections.TryGetValue(collectionId, out var collection))
        {
            collection.Title = title;
            await SaveAsync();
            Notify();
        }
    }

    public async Task RenamePage(Guid pageId, string title)
    {
        if (Doc.Pages.TryGetValue(pageId, out var page))
        {
            page.Title = title;
            await SaveAsync();
            Notify();
        }
    }

    // Reorder pages within a collection
    public async Task MovePage(Guid collectionId, Guid pageId, int delta)
    {
        if (!Doc.Collections.TryGetValue(collectionId, out var collection)) return;
        var i = collection.PageIds.FindIndex(id => id == pageId);
        if (i < 0) return;
        var j = Math.Clamp(i + delta, 0, collection.PageIds.Count - 1);
        if (i == j) return;
        (collection.PageIds[i], collection.PageIds[j]) = (collection.PageIds[j], collection.PageIds[i]);
        await SaveAsync();
        Notify();
    }

    public async Task DeletePage(Guid collectionId, Guid pageId)
    {
        if (!Doc.Collections.TryGetValue(collectionId, out var collection)) return;
        collection.PageIds.Remove(pageId);
        Doc.Pages.Remove(pageId);
        if (SelectedPageId == pageId)
            SelectedPageId = collection.PageIds.LastOrDefault();
        await SaveAsync();
        Notify();
    }

    // Clone a page
    public async Task<Guid> ClonePage(Guid collectionId, Guid pageId)
    {
        if (!Doc.Pages.TryGetValue(pageId, out var page)) return Guid.Empty;
        var clone = new Page
        {
            Title = page.Title + " (copy)",
            Snippets = page.Snippets.Select(s => s with { Id = Guid.NewGuid() }).ToList()
        };
        Doc.Pages[clone.Id] = clone;
        if (Doc.Collections.TryGetValue(collectionId, out var collection))
            collection.PageIds.Insert(collection.PageIds.IndexOf(pageId) + 1, clone.Id);
        await SaveAsync();
        Notify();
        return clone.Id;
    }

// 👉 Paste HTML into a Collection: new Page with one Snippet from clipboard HTML
    public async Task<Guid> PasteSnippetIntoCollection(Guid collectionId, string html, string? pageTitle = null)
    {
        if (string.IsNullOrWhiteSpace(html)) return Guid.Empty;

        // 1) sanitize (AngleSoft)
        var cleaned = _filter.Filter(html);

        // 2) ensure puids so WorkPane toolbars work immediately
        cleaned = await HtmlPuid.EnsurePuidsAsync(cleaned);

        var page = new Page
        {
            Title = pageTitle ?? $"Pasted {DateTime.Now:yyyy-MM-dd HH:mm}",
            Snippets = new() { new Snippet { Html = cleaned } }
        };
        Doc.Pages[page.Id] = page;
        if (Doc.Collections.TryGetValue(collectionId, out var collection))
            collection.PageIds.Add(page.Id);

        SelectedPageId = page.Id;
        SelectedSnippetId = page.Snippets.First().Id;

        await SaveAsync();
        Notify();
        return page.Id;
    }

    public async Task<Guid> AddSnippetToPage(Guid pageId, string html)
    {
        if (!Doc.Pages.TryGetValue(pageId, out var page)) return Guid.Empty;
        var cleaned = _filter.Filter(html);
        cleaned = await HtmlPuid.StripPuidsAsync(cleaned);
        cleaned = await HtmlPuid.EnsurePuidsAsync(cleaned);

        var snip = new Snippet { Html = cleaned };
        page.Snippets.Add(snip);

        SelectedPageId = pageId;
        SelectedSnippetId = snip.Id;

        await SaveAsync();
        Notify();
        return snip.Id;
    }

    public async Task<Guid> PasteSnippetIntoSelectedPage(string html)
    {
        if (SelectedPageId is not Guid pid) return Guid.Empty;
        return await AddSnippetToPage(pid, html);
    }

}
