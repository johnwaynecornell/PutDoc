// Services/HtmlTransformService.cs

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop; // for .ToHtml()

namespace PutDoc.Services;
//pc06d9280230f4fe6b9480035d0726cd6
public static class HtmlTransformService
{
    public static string SerializeFragment(INode root)
    {
        // Preserves text nodes, comments, and elements—plus their whitespace.
        return string.Concat(root.ChildNodes.Select(n => n.ToHtml()));
    }
    
    
    static readonly IBrowsingContext Ctx =
        BrowsingContext.New(Configuration.Default.WithDefaultLoader());
    
    static IElement? FindByPuid(INode root, string puid)
    {
        if (root is null || string.IsNullOrWhiteSpace(puid)) return null;
        // Note: attribute name is "data-puid" (no leading 's')
        return (root as IParentNode)?
            .QuerySelector($"[data-puid=\"{puid}\"]") as IElement;
    }

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
        foreach (var el in root.QuerySelectorAll(HtmlPuid.query))
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

            case "up":
                if (target.PreviousElementSibling is { } prev)
                {
                    prev.Insert(AdjacentPosition.BeforeBegin, target.OuterHtml);
                    target.Remove();
                    changed = true;
                }
                break;

            case "down":
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
                var justPersistPuids = SerializeFragment(root);
                await state.SetSnippetHtml(justPersistPuids);
                state.SelectSnippet(snippetId);
            }
            return true;
        }

        // After structural changes, ensure uniqueness and persist
        ReassignDuplicatePuids(root);
        var newHtml = SerializeFragment(root);
        await state.SetSnippetHtml(newHtml, false);
        state.SelectSnippet(snippetId);
        return true;
    }

public static async Task<string?> ExtractFragmentInnerByPuidAsync(string html, string puid)
{
    var doc = await Ctx.OpenAsync(req => req.Content(html));
    var el  = doc.QuerySelector($"[data-puid=\"{puid}\"]") as IElement;
    return el?.InnerHtml;
}

public static async Task<string?> ExtractFragmentOuterByPuidAsync(string html, string puid)
{
    var doc = await Ctx.OpenAsync(req => req.Content(html));
    var el  = doc.QuerySelector($"[data-puid=\"{puid}\"]") as IElement;
    return el?.OuterHtml;
}

/// <summary>
/// Replaces either the innerHTML or the outer element by PUID.
/// Guarantees the resulting element retains the same data-puid when replacing outer.
/// </summary>
public static async Task<string?> ReplaceFragmentByPuidAsync(
    string html, string puid, string replacement, bool replaceOuter)
{
    var doc = await Ctx.OpenAsync(req => req.Content(html));
    var el  = doc.QuerySelector($"[data-puid=\"{puid}\"]") as IElement;
    if (el is null) return null;

    if (replaceOuter)
    {
        // parse replacement as HTML
        var wrapper = doc.CreateElement("div");
        wrapper.InnerHtml = replacement;

        // pick the first element child; if none, bail
        var newEl = wrapper.FirstElementChild;
        if (newEl is null) return null;

        // ensure we keep same PUID
        if (!newEl.HasAttribute("data-puid"))
            newEl.SetAttribute("data-puid", puid);

        el.Replace(newEl);
    }
    else
    {
        el.InnerHtml = replacement;
    }

    // Serialize preserving text/whitespace (use your existing serializer)
    var root = doc.Body ?? doc.DocumentElement;
    return SerializeFragment(root);
    //return await HtmlSerialize.PreserveAllAsync(root!); // <- use the same method you already adopted
}

}
