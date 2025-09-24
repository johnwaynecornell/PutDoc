// PutDoc.Services/PutDocState.cs
namespace PutDoc.Services;

public class PutDocState
{
    public PutDocFile Doc { get; private set; } = new();
    public Guid? SelectedPageId { get; set; }
    public Guid? SelectedSnippetId { get; set; }

    readonly IPutDocStore _store;
    readonly IAngleSoftFilter _filter;

    // NEW: change event
    public event Action? Changed;
    void Notify() => Changed?.Invoke();

    // NEW: select snippet + notify others to re-render
    public void SelectSnippet(Guid? id)
    {
        SelectedSnippetId = id;
        Notify();
    }
    
    public void SelectPage(Guid id)
    {
        SelectedPageId = id;
        SelectedSnippetId = CurrentPage()?.Snippets.FirstOrDefault()?.Id;
        Notify();
    }

    public PutDocState(IPutDocStore store, IAngleSoftFilter filter)
    {
        _store = store; _filter = filter;
    }

    
    public async Task LoadAsync()
    {
        Doc = await _store.LoadAsync();
        SelectedPageId ??= Doc.Pages.Keys.FirstOrDefault();
        SelectedSnippetId ??= CurrentPage()?.Snippets.FirstOrDefault()?.Id;
        Notify();                      // notify after load too
    }
    
    public Page? CurrentPage() => SelectedPageId is Guid id && Doc.Pages.TryGetValue(id, out var p) ? p : null;
    public Snippet? CurrentSnippet() => CurrentPage()?.Snippets.FirstOrDefault(s => s.Id == SelectedSnippetId);

    public async Task SaveAsync()
    {
        await _store.SaveAsync(Doc);
        Notify();
    }

    public async Task SetSnippetHtml(string html)
    {
        var s = CurrentSnippet();
        if (s is null) return;
        s.Html = _filter.Filter(html);
        await SaveAsync();             // SaveAsync will Notify()
    }

    public async Task AddSnippetBelow(Guid? afterId = null)
    {
        var page = CurrentPage(); if (page is null) return;
        var idx = afterId is Guid a ? page.Snippets.FindIndex(x => x.Id == a) : page.Snippets.Count - 1;
        var insertAt = Math.Clamp(idx + 1, 0, page.Snippets.Count);
        page.Snippets.Insert(insertAt, new Snippet());
        await SaveAsync();
    }

    public async Task DeleteSnippet(Guid id)
    {
        var page = CurrentPage(); if (page is null) return;
        var i = page.Snippets.FindIndex(s => s.Id == id);
        if (i >= 0) page.Snippets.RemoveAt(i);
        SelectedSnippetId = page.Snippets.ElementAtOrDefault(Math.Min(i, page.Snippets.Count - 1))?.Id;
        await SaveAsync();
    }

    public async Task CloneSnippet(Guid id)
    {
        var page = CurrentPage(); if (page is null) return;
        var i = page.Snippets.FindIndex(s => s.Id == id);
        if (i < 0) return;
        var clone = page.Snippets[i] with { Id = Guid.NewGuid() };
        page.Snippets.Insert(i + 1, clone);
        await SaveAsync();
    }

    public async Task MoveSnippet(Guid id, int delta)
    {
        var page = CurrentPage(); if (page is null) return;
        var i = page.Snippets.FindIndex(s => s.Id == id);
        if (i < 0) return;
        var j = Math.Clamp(i + delta, 0, page.Snippets.Count - 1);
        if (i == j) return;
        (page.Snippets[i], page.Snippets[j]) = (page.Snippets[j], page.Snippets[i]);
        await SaveAsync();
    }
    
    // 🔸 selection edit model
    public sealed class SelectionEdit
    {
        public bool IsActive { get; set; }
        public Guid SnippetId { get; set; }
        public string Selector { get; set; } = "";
        public string Html { get; set; } = "";      // fragment outerHTML
    }

    public SelectionEdit Selection { get; } = new();  // expose read-only object

    public void BeginSelectionEdit(Guid snippetId, string selector, string html)
    {
        Selection.IsActive = true;
        Selection.SnippetId = snippetId;
        Selection.Selector = selector;
        Selection.Html = html;
        SelectedSnippetId = snippetId;  // make sure editor focuses this snippet
        Notify();
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

}
