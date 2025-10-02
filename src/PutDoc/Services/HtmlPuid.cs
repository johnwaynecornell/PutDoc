// Services/HtmlPuid.cs
using AngleSharp;
using AngleSharp.Dom;

namespace PutDoc.Services;

public static class HtmlPuid
{
    static readonly IBrowsingContext Ctx =
        BrowsingContext.New(Configuration.Default.WithDefaultLoader());

    public static string query = ".slf-card, .slf-brick, .prompt_area, pre, ul, ol, li";
    
    static void EnsurePuid(IElement el)
    {
        if (string.IsNullOrWhiteSpace(el.GetAttribute("data-puid")))
            el.SetAttribute("data-puid", "p" + Guid.NewGuid().ToString("N"));
    }

    public static async Task<string> EnsurePuidsAsync(string html)
    {
        var doc = await Ctx.OpenAsync(r => r.Content(html ?? ""));
        var root = doc.Body!;
        foreach (var el in root.QuerySelectorAll(query))
            EnsurePuid(el);
        // Serialize fragment
        return HtmlTransformService.SerializeFragment(root);
    }

    public static async Task<string> StripPuidsAsync(string html)
    {
        var doc = await Ctx.OpenAsync(r => r.Content(html ?? ""));
        var root = doc.Body!;
        foreach (var el in root.QuerySelectorAll("[data-puid]"))
            el.RemoveAttribute("data-puid");
        return HtmlTransformService.SerializeFragment(root);
    }
}