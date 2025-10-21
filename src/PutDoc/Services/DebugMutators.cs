// Services/DebugMutators.cs

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Hosting;
using PutDoc.Services;

public sealed class DebugMutators
{
    private readonly PutDocState _state;
    private readonly IHostEnvironment _env;
    private readonly NavigationManager _nav;

    public DebugMutators(PutDocState state, IHostEnvironment env, NavigationManager nav)
    {
        _state = state; _env = env;
        _nav = nav;
    }

    private void EnsureDev()
    {
        if (!_env.IsDevelopment())
            throw new InvalidOperationException("Debug mutators are only available in Development.");
    }

    // 1) Insert a missing page reference at index (keeps order)
    public void AddMissingPageRef(Guid collectionId, int atIndex = 0)
    {
        EnsureDev();
        var doc = _state.Doc;
        if (!doc.Collections.TryGetValue(collectionId, out var col))
            return;

        var ghost = Guid.NewGuid(); // guaranteed missing
        col.PageIds ??= new List<Guid>();
        atIndex = Math.Clamp(atIndex, 0, col.PageIds.Count);
        col.PageIds.Insert(atIndex, ghost);

        AfterMutate();
    }

    // 2) Duplicate the first page reference
    public void DuplicateFirstPageRef(Guid collectionId)
    {
        EnsureDev();
        var doc = _state.Doc;
        if (!doc.Collections.TryGetValue(collectionId, out var col)) return;
        if (col.PageIds is null || col.PageIds.Count == 0) return;
        col.PageIds.Insert(1, col.PageIds[0]); // duplicate next to it
        AfterMutate();
    }

    // 3) Remove an existing page object but keep references (becomes “missing”)
    public void RemovePageObjectKeepRefs(Guid pageId)
    {
        EnsureDev();
        _state.Doc.Pages.Remove(pageId);
        AfterMutate();
    }

    // 4) Insert an invalid child collection ref
    public void AddMissingChildCollectionRef(Guid collectionId)
    {
        EnsureDev();
        var doc = _state.Doc;
        if (!doc.Collections.TryGetValue(collectionId, out var col)) return;
        var ghost = Guid.NewGuid();
        col.ChildCollectionIds ??= new List<Guid>();
        col.ChildCollectionIds.Add(ghost);
        AfterMutate();
    }

    // 5) Scramble page order within a collection (to verify order is preserved on repair)
    public void ShufflePages(Guid collectionId)
    {
        EnsureDev();
        var doc = _state.Doc;
        if (!doc.Collections.TryGetValue(collectionId, out var col)) return;
        if (col.PageIds is null || col.PageIds.Count < 2) return;
        col.PageIds = col.PageIds.OrderBy(_ => Guid.NewGuid()).ToList();
        AfterMutate();
    }

    // 6) Add an orphan page not referenced anywhere
    public void AddOrphanPage()
    {
        EnsureDev();
        var id = Guid.NewGuid();
        _state.Doc.Pages[id] = new Page { Id = id, Title = $"Orphan {id.ToString()[..8]}", Snippets = new() };
        AfterMutate();
    }

    // — common tail: flag for repair & notify —
    private async void AfterMutate()
    {
        await _state.SaveWholeDocumentAsync();
        _nav.NavigateTo($"/doc/{_state.Meta.Id}", forceLoad: true);//await _state.EnsureLoadedAsync(_state.CurrentDocId);
    }
}
