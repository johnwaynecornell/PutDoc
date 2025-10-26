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
    private readonly RepairLogService _repairs;
    public PutDocState(RepairLogService repairs, IDocCatalogService catalog, IAngleSoftFilter filter, IJSRuntime js)
    {
        _repairs = repairs;
        _catalog = catalog;
        _filter = filter;
        _js = js;
    }

    // PutDocState.cs
    private bool? _isWriteBlocked = null;
    private bool _needsRepairReview;


    public bool NeedsRepairReview
    {
        get => _needsRepairReview;
        private set
        {
            if (_needsRepairReview == value) return;
            _needsRepairReview = value;
            UpdateReadOnlyAndNotifyIfChanged();
        }
    }

    public bool IsReadOnly => _needsRepairReview || (_isWriteBlocked != null ? (bool) _isWriteBlocked : false);
    
// --- Public API ---

    public void SetWriteBlock(bool blocked)
    {
        if (_isWriteBlocked != null && ((bool)_isWriteBlocked == blocked)) return;
        _isWriteBlocked = blocked;
        UpdateReadOnlyAndNotifyIfChanged();
    }

// Use this from the repair banner / loader:
    public void SetNeedsRepairReview(bool needsReview)
    {
        if (_needsRepairReview == needsReview) return;
        _needsRepairReview = needsReview;
        UpdateReadOnlyAndNotifyIfChanged();
    }

// Atomic update (prevents double notify)
    public void SetRepairReviewAndWriteBlock(bool needsReview, bool blockedWhenNoReview)
    {
        var oldEffective = IsReadOnly;

        _needsRepairReview = needsReview;
        _isWriteBlocked    = blockedWhenNoReview;

        // Only notify + DOM update if effective changed
        if (oldEffective != IsReadOnly)
            UpdateDomReadOnlyAttrAndNotify();
        else
            Notify(); // state changed but effective lock didn’t; still safe to coalesce
    }

// --- Internals ---

    private bool _lastDomReadOnly = true; // track what we last wrote to DOM

    private void UpdateReadOnlyAndNotifyIfChanged()
    {
        if (_lastDomReadOnly != IsReadOnly)
        {
            UpdateDomReadOnlyAttrAndNotify();
        }
        else
        {
            Notify();
        }
    }

    private void UpdateDomReadOnlyAttrAndNotify()
    {
        _lastDomReadOnly = IsReadOnly;
        // fire & forget on purpose; ignore occasional hydration race
        _ = _js.InvokeVoidAsync(
            "document.documentElement.setAttribute",
            "data-pd-readonly",
            IsReadOnly ? "true" : "false");

        Notify();
    }

    
    public bool IsDirty { get; private set; }

    public void MarkDirty()
    {
        if (IsDirty) return;
        IsDirty = true;
        Notify();
    }

    public void ClearDirty()
    {
        if (!IsDirty) return;
        IsDirty = false;
        Notify();
    }


    // NEW: change event
    public event Action? Changed;

    private int _batchDepth = 0;
    private bool _pendingNotify = false;

    // fire immediately (if not batching)
    public void Notify()
    {
        if (_batchDepth > 0)
        {
            _pendingNotify = true;
            return;
        }

        Changed?.Invoke();
    }

    // run a block and notify once at the end
    public void Notify(Action mutation)
    {
        using (BeginBatch()) mutation();
        // BeginBatch/Dispose will emit once
    }

    // async variant
    public async Task NotifyAsync(Func<Task> mutation)
    {
        using (BeginBatch()) await mutation();
    }

    public event Action? UiReset;

    public void TriggerUiReset()
    {
        UiReset?.Invoke();
        
        //_isWriteBlocked = null;
        _needsRepairReview = false;
    }

    // explicit batch (nestable)
    public IDisposable BeginBatch() => new BatchToken(this);

    private sealed class BatchToken : IDisposable
    {
        private readonly PutDocState _s;
        private bool _disposed;

        public BatchToken(PutDocState s)
        {
            _s = s;
            _s._batchDepth++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (--_s._batchDepth == 0 && _s._pendingNotify)
            {
                _s._pendingNotify = false;
                _s.Changed?.Invoke();
            }
        }
    }


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
            Notify();
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


    public static class PutDocNormalize
    {
        public sealed class RepairSummary
        {
            public int MissingPagesRecovered { get; set; }
            public int MissingCollectionsRecovered { get; set; }
            public int PageRefsDeduped { get; set; }
            public int CollectionRefsDeduped { get; set; }

            public int OrphanPagesAttached { get; set; }
            public int OrphanCollectionsAttached { get; set; }

            public List<Guid> RecoveredPageIds { get; } = new();
            public List<Guid> RecoveredCollectionIds { get; } = new();

            // (Optional) map: originalId -> newId when RekeyNewIds
            public Dictionary<Guid, Guid> RekeyedIds { get; } = new();

            public bool Any =>
                MissingPagesRecovered + MissingCollectionsRecovered +
                PageRefsDeduped + CollectionRefsDeduped +
                OrphanPagesAttached + OrphanCollectionsAttached > 0;

            public override string ToString()
            {
                var parts = new List<string>();
                if (MissingPagesRecovered > 0) parts.Add($"{MissingPagesRecovered} page(s) recovered");
                if (MissingCollectionsRecovered > 0) parts.Add($"{MissingCollectionsRecovered} collection(s) recovered");
                if (PageRefsDeduped > 0) parts.Add($"{PageRefsDeduped} duplicate page ref(s) removed");
                if (CollectionRefsDeduped > 0) parts.Add($"{CollectionRefsDeduped} duplicate collection ref(s) removed");
                if (OrphanPagesAttached > 0) parts.Add($"{OrphanPagesAttached} orphan page(s) attached to Lost & Found");
                if (OrphanCollectionsAttached > 0) parts.Add($"{OrphanCollectionsAttached} orphan collection(s) attached to Lost & Found");
                return parts.Count == 0 ? "No repairs needed." : string.Join("; ", parts) + ".";
            }
        }

        public enum RecoveryStrategy
        {
            RetainIds,
            RekeyNewIds
        }

        public static RepairSummary NormalizeDocument(
            PutDocFile doc,
            RecoveryStrategy strategy = RecoveryStrategy.RetainIds,
            string recoveredSuffix = " (recovered)")
        {
            var report = new RepairSummary();
            if (doc is null) return report;
        
            // Ensure root exists
            if (!doc.Collections.ContainsKey(doc.RootCollectionId) && doc.Collections.Count > 0)
                doc.RootCollectionId = doc.Collections.Keys.First();
        
            static string NameFor(string kind, Guid id, string suf)
                => $"{kind} {id.ToString()[..8]}{suf}";
        
            var collectionsToAdd = new List<Collection>();
        
            // Track references as we walk
            var referencedPageIds = new HashSet<Guid>();
            var referencedCollectionIds = new HashSet<Guid> { doc.RootCollectionId }; // root is referenced by definition
        
            // -------- First pass: fix refs (dedupe/recover) and collect reference sets --------
            foreach (var c in doc.Collections.Values)
            {
                // Pages
                if (c.PageIds is { Count: > 0 })
                {
                    var seen = new HashSet<Guid>();
                    for (int i = 0; i < c.PageIds.Count; i++)
                    {
                        var pid = c.PageIds[i];
        
                        if (!seen.Add(pid))
                        {
                            c.PageIds.RemoveAt(i);
                            i--; report.PageRefsDeduped++;
                            continue;
                        }
        
                        referencedPageIds.Add(pid);
        
                        if (!doc.Pages.ContainsKey(pid))
                        {
                            var newId = (strategy == RecoveryStrategy.RetainIds) ? pid : Guid.NewGuid();
                            if (strategy == RecoveryStrategy.RekeyNewIds && newId != pid)
                            {
                                report.RekeyedIds[pid] = newId;
                                c.PageIds[i] = newId;
                            }
        
                            if (!doc.Pages.ContainsKey(newId))
                            {
                                doc.Pages[newId] = new Page
                                {
                                    Id = newId,
                                    Title = NameFor("Page", newId, recoveredSuffix),
                                    Snippets = new List<Snippet>()
                                };
                                report.MissingPagesRecovered++;
                                report.RecoveredPageIds.Add(newId);
                            }
        
                            referencedPageIds.Add(newId);
                        }
                    }
                }
        
                // Child Collections
                if (c.ChildCollectionIds is { Count: > 0 })
                {
                    var seen = new HashSet<Guid>();
                    for (int i = 0; i < c.ChildCollectionIds.Count; i++)
                    {
                        var cid = c.ChildCollectionIds[i];
        
                        if (!seen.Add(cid))
                        {
                            c.ChildCollectionIds.RemoveAt(i);
                            i--; report.CollectionRefsDeduped++;
                            continue;
                        }
        
                        referencedCollectionIds.Add(cid);
        
                        if (!doc.Collections.ContainsKey(cid))
                        {
                            var newId = (strategy == RecoveryStrategy.RetainIds) ? cid : Guid.NewGuid();
                            if (strategy == RecoveryStrategy.RekeyNewIds && newId != cid)
                            {
                                report.RekeyedIds[cid] = newId;
                                c.ChildCollectionIds[i] = newId;
                            }
        
                            if (!doc.Collections.ContainsKey(newId))
                            {
                                collectionsToAdd.Add(new Collection
                                {
                                    Id = newId,
                                    Title = NameFor("Collection", newId, recoveredSuffix),
                                    PageIds = new List<Guid>(),
                                    ChildCollectionIds = new List<Guid>()
                                });
        
                                report.MissingCollectionsRecovered++;
                                report.RecoveredCollectionIds.Add(newId);
                            }
        
                            referencedCollectionIds.Add(newId);
                        }
                    }
                }
            }
        
            // Materialize any recovered collections we staged
            foreach (var c in collectionsToAdd)
                doc.Collections[c.Id] = c;
        
            // -------- Second pass: attach ORPHANS to Lost & Found --------
        
            // Build the set of all referenced pages/collections from the whole tree we just fixed
            referencedPageIds.Clear();
            referencedCollectionIds.Clear();
            referencedCollectionIds.Add(doc.RootCollectionId);
        
            foreach (var c in doc.Collections.Values)
            {
                if (c.PageIds is { Count: > 0 })
                    foreach (var pid in c.PageIds) referencedPageIds.Add(pid);
        
                if (c.ChildCollectionIds is { Count: > 0 })
                    foreach (var cid in c.ChildCollectionIds) referencedCollectionIds.Add(cid);
            }
        
            // Orphan pages = in Pages but in no collection.PageIds
            var orphanPages = doc.Pages.Keys.Where(pid => !referencedPageIds.Contains(pid)).ToList();
        
            // Orphan collections = in Collections but not root and not referenced by any parent's ChildCollectionIds
            var orphanCollections = doc.Collections.Keys
                .Where(cid => cid != doc.RootCollectionId && !referencedCollectionIds.Contains(cid))
                .ToList();
        
            if (orphanPages.Count > 0 || orphanCollections.Count > 0)
            {
                // Find or create a single Lost & Found under root
                var lfId = doc.Collections.Values
                    .FirstOrDefault(kv => kv.Title.Equals("Lost & Found", StringComparison.OrdinalIgnoreCase))?.Id
                    ?? Guid.NewGuid();
        
                if (!doc.Collections.ContainsKey(lfId))
                {
                    doc.Collections[lfId] = new Collection
                    {
                        Id = lfId,
                        Title = "Lost & Found",
                        PageIds = new List<Guid>(),
                        ChildCollectionIds = new List<Guid>()
                    };
        
                    // Attach Lost & Found under root if not already present
                    var root = doc.Collections[doc.RootCollectionId];
                    root.ChildCollectionIds ??= new List<Guid>();
                    if (!root.ChildCollectionIds.Contains(lfId))
                        root.ChildCollectionIds.Add(lfId);
                }
        
                var lf = doc.Collections[lfId];
                lf.PageIds ??= new List<Guid>();
                lf.ChildCollectionIds ??= new List<Guid>();
        
                // Append orphan pages
                foreach (var pid in orphanPages)
                {
                    lf.PageIds.Add(pid);
                    report.OrphanPagesAttached++;
                }
        
                // Append orphan collections (avoid self/root)
                foreach (var cid in orphanCollections)
                {
                    if (cid == lfId || cid == doc.RootCollectionId) continue;
                    lf.ChildCollectionIds.Add(cid);
                    report.OrphanCollectionsAttached++;
                }
            }
        
            return report;
        }

        
        public static RepairSummary NormalizeDocumentOrig(
            PutDocFile doc,
            RecoveryStrategy strategy = RecoveryStrategy.RetainIds,
            string recoveredSuffix = " (recovered)")
        {
            var report = new RepairSummary();
            if (doc is null) return report;

            if (!doc.Collections.ContainsKey(doc.RootCollectionId) && doc.Collections.Count > 0)
                doc.RootCollectionId = doc.Collections.Keys.First();

            static string NameFor(string kind, Guid id, string suf)
                => $"{kind} {id.ToString()[..8]}{suf}";

            List<Collection> collectionsToBeAdded = new List<Collection>();
            
            foreach (var c in doc.Collections.Values)
            {
                // PageIds: dedupe (keep first), recover missing—preserving order
                if (c.PageIds is { Count: > 0 })
                {
                    var seen = new HashSet<Guid>();
                    for (int i = 0; i < c.PageIds.Count; i++)
                    {
                        var pid = c.PageIds[i];

                        if (!seen.Add(pid))
                        {
                            c.PageIds.RemoveAt(i);
                            i--;
                            report.PageRefsDeduped++;
                            continue;
                        }

                        if (!doc.Pages.ContainsKey(pid))
                        {
                            var newId = (strategy == RecoveryStrategy.RetainIds) ? pid : Guid.NewGuid();
                            if (strategy == RecoveryStrategy.RekeyNewIds && newId != pid)
                            {
                                report.RekeyedIds[pid] = newId;
                                c.PageIds[i] = newId; // swap in place, same index
                            }

                            if (!doc.Pages.ContainsKey(newId))
                            {
                                doc.Pages[newId] = new Page
                                {
                                    Id = newId,
                                    Title = NameFor("Page", newId, recoveredSuffix),
                                    Snippets = new List<Snippet>()
                                };
                                report.MissingPagesRecovered++;
                                report.RecoveredPageIds.Add(newId);
                            }
                        }
                    }
                }

                // ChildCollectionIds: dedupe (keep first), recover missing—preserving order
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
                            report.CollectionRefsDeduped++;
                            continue;
                        }

                        if (!doc.Collections.ContainsKey(cid))
                        {
                            var newId = (strategy == RecoveryStrategy.RetainIds) ? cid : Guid.NewGuid();
                            if (strategy == RecoveryStrategy.RekeyNewIds && newId != cid)
                            {
                                report.RekeyedIds[cid] = newId;
                                c.ChildCollectionIds[i] = newId;
                            }

                            if (!doc.Collections.ContainsKey(newId))
                            {
                                collectionsToBeAdded.Add( new Collection
                                {
                                    Id = newId,
                                    Title = NameFor("Collection", newId, recoveredSuffix),
                                    PageIds = new List<Guid>(),
                                    ChildCollectionIds = new List<Guid>()
                                });
                                
                                report.MissingCollectionsRecovered++;
                                report.RecoveredCollectionIds.Add(newId);
                            }
                        }
                    }
                }
                
            }
            
            foreach (Collection c in collectionsToBeAdded)
            {
                doc.Collections[c.Id]=c;
            }

            return report;
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

    public PutDocNormalize.RepairSummary? LastRepairSummary { get; private set; }

    /*
     await JS.InvokeVoidAsync("eval", "window.putdocEnh.getHub()?.invokeMethodAsync('AcquireDocWriter', true)");
     await JS.InvokeVoidAsync("eval", "window.putdocEnh.getHub()?.invokeMethodAsync('ReleaseDocWriter')");
    */
    
    
    public async Task ApplyRepairsAndSaveAsync()
    {
        if (Meta is null) return;

        // Normalize the live Doc in place (order-preserving recovery)
        var summary = PutDocNormalize.NormalizeDocument(Doc);

        
        await NotifyAsync(async () =>
        {
            SetNeedsRepairReview(false);
            // clear write-block unless other guards apply
            var v = await _js.InvokeAsync<JsonElement>("putdocPresence.acquireWriter", true);
            var isWriter = v.ValueKind == JsonValueKind.Object
                           && v.TryGetProperty("status", out var s)
                           && s.GetString() == "writer";
            if (isWriter)
                await SaveWholeDocumentAsync();//if (!ok) SetWriteBlock(true); // fallback: remain read-only if presence denied
        });
    }

    public async Task IgnoreRepairsKeepReadOnly()
    {
        await NotifyAsync(async () =>
        {
            SetNeedsRepairReview(false);   // hide banner
            await _js.InvokeVoidAsync("putdocPresence.releaseWriter");
            //SetWriteBlock(true);           // explicitly keep read-only
        });
    }

    public async Task DismissAndEditAnyway()
    {
        await NotifyAsync(async () =>
        {
            SetNeedsRepairReview(false);   // hide banner
            var ok = await _js.InvokeAsync<JsonElement>("putdocPresence.acquireWriter", true);
            //if (!ok) SetWriteBlock(true);
        });
    }
    public static async Task<PutDocFile> DeepClone(PutDocFile file)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        using var fs = ConvertStringToStream(JsonSerializer.Serialize(file, jsonOptions));
        var loadedDoc = await JsonSerializer.DeserializeAsync<PutDocFile>(fs, jsonOptions)
                        ?? new PutDocFile();
        fs.Close();

        return loadedDoc;

    }

    public async Task LoadAsync(Guid id)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };


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

            using var fs = ConvertStringToStream(payload.json);
            var loadedDoc = await JsonSerializer.DeserializeAsync<PutDocFile>(fs, jsonOptions)
                            ?? new PutDocFile();

            LogDocAnomalies(loadedDoc);

            //LastRepairSummary = PutDocNormalize.NormalizeDocument(loadedDoc);

            //if (LastRepairSummary?.Any == true)
            //    Console.WriteLine($"[repair] {LastRepairSummary}");

            // Ensure puids in all snippets
            foreach (var p in loadedDoc.Pages.Values)
            {
                for (int i = 0; i < p.Snippets.Count; i++)
                {
                    p.Snippets[i].Html = await HtmlPuid.EnsurePuidsAsync(p.Snippets[i].Html ?? "");
                }
            }

            var previewCopy = await DeepClone(loadedDoc); // serialize/deserialize or manual copy
            var previewSummary = PutDocNormalize.NormalizeDocument(
                previewCopy, PutDocNormalize.RecoveryStrategy.RetainIds /* or RekeyNewIds */);

            _repairs.Record(id, previewSummary);

            Notify(() =>
            {
                try
                {

                    // ---- Apply atomically to state (this is the only mutation point) ----
                    Doc = loadedDoc;
                    Meta = meta with { Version = payload.version };

                    // Reset selection for the new doc (don’t carry old ids forward)
                    SelectedPageId = Doc.Collections.TryGetValue(Doc.RootCollectionId, out var root) &&
                                     root.PageIds is { Count: > 0 }
                        ? root.PageIds[0]
                        : Doc.Pages.Keys.FirstOrDefault();

                    SelectedSnippetId = (SelectedPageId is Guid pid && Doc.Pages.TryGetValue(pid, out var page) &&
                                         page.Snippets.Count > 0)
                        ? page.Snippets[0].Id
                        : null;
                    //SelectedSnippetId = CurrentPage()?.Snippets.FirstOrDefault()?.Id;

                    _appliedEpoch = myEpoch;

                    // If repairs are needed, lock editing until reviewed
                    SetNeedsRepairReview(previewSummary?.Any == true);
                    
                    //SetNeedsRepairReview( true);
                    //if (NeedsRepairReview) SetWriteBlock(true);

                    // Mark clean and announce an EXTERNAL content change so listeners refresh
                    LastUpdateSource =
                        UpdateSource.External; // ← make sure this property exists as in your editor logic
                    ContentVersion++; // ← bump the version so sync code sees a change
                    Console.WriteLine($"[State] Load APPLY id={id} myEpoch={myEpoch} cv={ContentVersion}");
                    IsDirty = false;
                }
                catch (OperationCanceledException)
                {
                    // ignored
                }
            });

        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }


// Call this from wherever you persist the whole document
    public async Task SaveWholeDocumentAsync(bool force=false)
    {
        if (IsReadOnly && !force) return;

        var id = CurrentDocId;
        if (id == Guid.Empty) throw new InvalidOperationException("No current document");

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(Doc, jsonOptions); // your existing serializer
        var expected = DocVersion; // CAS

        await _catalog.SaveDocumentAsync(id, json, expectedVersion: expected);

        // reflect bump locally
        Meta = Meta! with { Version = expected + 1, Modified = DateTimeOffset.UtcNow };
        ClearDirty();
        Notify();
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
