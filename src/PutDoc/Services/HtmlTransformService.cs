// Services/HtmlTransformService.cs
using AngleSharp;
using AngleSharp.Dom;

namespace PutDoc.Services;

public static class HtmlTransformService
{
    static readonly IBrowsingContext Ctx =
        BrowsingContext.New(Configuration.Default.WithDefaultLoader());
    
     static IElement? FindByPuid(INode root, string puid) =>
        (root as IElement)?.QuerySelector($"[sdata-puid=\"{puid}\"]");

    static IElement? FindByPath(IElement root, string path) =>
        string.IsNullOrWhiteSpace(path) ? null : root.QuerySelector(path);

    static void EnsurePuid(IElement el)
    {
        if (string.IsNullOrWhiteSpace(el.GetAttribute("data-puid")))
            el.SetAttribute("data-puid", "p" + Guid.NewGuid().ToString("N"));
    }

    static void ReassignDuplicatePuids(IElement scope)
    {
        var groups = scope.QuerySelectorAll("[data-puid]")
                          .GroupBy(e => e.GetAttribute("data-puid") ?? "");
        foreach (var g in groups)
        {
            bool first = true;
            foreach (var el in g)
            {
                if (first) { first = false; continue; }
                el.SetAttribute("data-puid", "p" + Guid.NewGuid().ToString("N"));
            }
        }
    }

    public static async Task<bool> ApplyAsync(PutDocState state, Guid snippetId, string action, string puid, string? path = null)
    {
        var page = state.CurrentPage(); if (page is null) return false;
        var snip = page.Snippets.FirstOrDefault(s => s.Id == snippetId); if (snip is null) return false;

        var doc = await Ctx.OpenAsync(req => req.Content(snip.Html ?? ""));
        var root = doc.Body!;

        // Ensure actionable elements at least have *some* puid (for future)
        foreach (var el in root.QuerySelectorAll(".slf-card, .slf-brick, .prompt_area, pre"))
            EnsurePuid(el);

        var target = FindByPuid(root, puid);

        // Fallback: resolve by cssPath and attach the incoming puid so future actions hit by puid.
        if (target is null && !string.IsNullOrWhiteSpace(path))
        {
            var byPath = FindByPath(root, path);
            if (byPath is not null)
            {
                byPath.SetAttribute("data-puid", puid);
                target = byPath;
            }
        }

        if (target is null) return false;

        bool changed = false;

        switch (action)
        {
            case "edit":
                state.BeginSelectionEdit(snippetId, puid /* store puid here */, target.OuterHtml);
                return true;

            case "clone":
                target.Insert(AdjacentPosition.AfterEnd, target.OuterHtml);
                changed = true;
                break;

            case "delete":
                target.Remove();
                changed = true;
                break;

            case "move-up":
                if (target.PreviousElementSibling is { } prev)
                {
                    prev.Insert(AdjacentPosition.BeforeBegin, target.OuterHtml);
                    target.Remove();
                    changed = true;
                }
                break;

            case "move-down":
                if (target.NextElementSibling is { } next)
                {
                    next.Insert(AdjacentPosition.AfterEnd, target.OuterHtml);
                    target.Remove();
                    changed = true;
                }
                break;
        }

        if (!changed)
        {
            // If we attached puid via path only, persist that so next time PUID lookup works
            if (FindByPuid(root, puid) is not null && !string.IsNullOrWhiteSpace(path))
            {
                var justPersistPuids = string.Concat(root.Children.Select(c => c.OuterHtml));
                await state.SetSnippetHtml(justPersistPuids);
                state.SelectSnippet(snippetId);
            }
            return true;
        }

        // After structural changes, ensure uniqueness and persist
        ReassignDuplicatePuids(root);
        var newHtml = string.Concat(root.Children.Select(c => c.OuterHtml));
        await state.SetSnippetHtml(newHtml);
        state.SelectSnippet(snippetId);
        return true;
    }

    public static async Task<string?> ReplaceFragmentByPuidAsync(string html, string puid, string newOuterHtml)
    {
        var doc = await Ctx.OpenAsync(req => req.Content(html ?? ""));
        var root = doc.Body!;
        var target = FindByPuid(root, puid);
        if (target is null) return null;
        target.OuterHtml = newOuterHtml ?? "";
        foreach (var el in root.QuerySelectorAll(".slf-card, .slf-brick, .prompt_area, pre"))
            EnsurePuid(el);
        ReassignDuplicatePuids(root);
        return string.Concat(root.Children.Select(c => c.OuterHtml));
    }
}
