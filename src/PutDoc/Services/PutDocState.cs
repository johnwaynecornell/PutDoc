// PutDoc.Services/PutDocState.cs
// PutDoc.Services/PutDocState.cs
namespace PutDoc.Services;

public class PutDocState
{
    public PutDocFile Doc { get; private set; } = new();
    public Guid? SelectedPageId { get; set; }
    public Guid? SelectedSnippetId { get; set; }

    public string CurrentUserId { get; set; } = new Guid().ToString();
    
    readonly IPutDocStore _store;
    readonly IAngleSoftFilter _filter;

    // NEW: change event
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();

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

    public PutDocState(IPutDocStore store, IAngleSoftFilter filter)
    {
        _store = store;
        _filter = filter;
    }

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

    public Page? CurrentPage() => SelectedPageId is Guid id && Doc.Pages.TryGetValue(id, out var p) ? p : null;
    public Snippet? CurrentSnippet() => CurrentPage()?.Snippets.FirstOrDefault(s => s.Id == SelectedSnippetId);

    public async Task SaveAsync()
    {
        await _store.SaveAsync(Doc);
        Notify();
    }
    
    public long ContentVersion { get; private set; }

    public enum UpdateSource { Editor, External }
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

    public enum FragmentScope { Inner, Outer }

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

        var page = CurrentPage(); if (page is null) return;
        var snip = page.Snippets.FirstOrDefault(s => s.Id == snippetId); if (snip is null) return;

        var fragHtml = scope == FragmentScope.Outer
            ? await HtmlTransformService.ExtractFragmentOuterByPuidAsync(snip.Html ?? "", puid)
            : await HtmlTransformService.ExtractFragmentInnerByPuidAsync(snip.Html ?? "", puid);

        Selection.IsActive  = true;
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
