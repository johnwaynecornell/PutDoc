// Services/HtmlTransformService.cs
using AngleSharp;
using AngleSharp.Dom;

namespace PutDoc.Services;

public static class HtmlTransformService
{
    static readonly IBrowsingContext Ctx =
        BrowsingContext.New(Configuration.Default.WithDefaultLoader());

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

    static IElement? FindByPuid(INode root, string puid) =>
        (root as IElement)?.QuerySelector($"[data-puid=\"{puid}\"]");

    public static async Task<string?> ReplaceFragmentByPuidAsync(string html, string puid, string newOuterHtml)
    {
        var doc = await Ctx.OpenAsync(req => req.Content(html ?? ""));
        var root = doc.Body!;
        var target = FindByPuid(root, puid);
        if (target is null) return null;

        // Replace and ensure uniqueness of puids (for clones)
        target.OuterHtml = newOuterHtml ?? "";
        foreach (var el in root.QuerySelectorAll(".slf-card, .slf-brick, .prompt_area, pre"))
            EnsurePuid(el);
        ReassignDuplicatePuids(root);

        return string.Concat(root.Children.Select(c => c.OuterHtml));
    }

    public static async Task<bool> ApplyAsync(PutDocState state, Guid snippetId, string action, string puid)
    {
        Console.WriteLine(action + " "+puid);
        
        
        
        
        var page = state.CurrentPage(); if (page is null) return false;
        var snip = page.Snippets.FirstOrDefault(s => s.Id == snippetId); if (snip is null) return false;

        var doc = await Ctx.OpenAsync(req => req.Content(snip.Html ?? ""));
        var root = doc.Body!;

        // Ensure every actionable element has a puid
        foreach (var el in root.QuerySelectorAll(".slf-card, .slf-brick, .prompt_area, pre"))
            EnsurePuid(el);

        var target = FindByPuid(root, puid);
        if (target is null) return false;

        bool changed = false;

        switch (action)
        {
            case "edit":
                state.BeginSelectionEdit(snippetId, puid, target.OuterHtml);
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

        if (!changed) return false;

        // Fix duplicate puids after structural ops
        ReassignDuplicatePuids(root);
        var newHtml = string.Concat(root.Children.Select(c => c.OuterHtml));

        state.SelectSnippet(snippetId);
        await state.SetSnippetHtml(newHtml);
        return true;
    }

    public static async Task<string?> ExtractFragmentByPuidAsync(string html, string puid)
    {
        var doc = await Ctx.OpenAsync(req => req.Content(html ?? ""));
        var root = doc.Body!;
        var target = FindByPuid(root, puid);
        return target?.OuterHtml;
    }
}
