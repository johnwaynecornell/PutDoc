// Services/HtmlPuid.cs

using System.Text;
using AngleSharp;
using AngleSharp.Dom;

namespace PutDoc.Services;

public static class HtmlPuid
{
    static readonly IBrowsingContext Ctx =
        BrowsingContext.New(Configuration.Default.WithDefaultLoader());

    public static string query = ".slf-card, .slf-brick, .prompt_area, pre, ul, ol, li, svg, table, p, h1, h2, h3, h4, h5";
    
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

    /*
     * parameter:
     * {
     *      htnl: the raw input with puids
     *      stripped: the raw input without puids
     * }
     *
     * output:
     * {
     *      List<object> ParsedPuidsAndStrings = {[Guid | String]*}
     * }
     */
    public static async Task<List<object>> ParsePuidsAsync(string html, string stripped)
    {
        StringBuilder? sb = null;

        List<object> ParsedPuidsAndStrings = new List<object>();
        
        int index_stripped = 0;
        int index_html = 0;

        while (index_stripped < stripped.Length)
        {
            if (stripped[index_stripped] == html[index_html])
            {
                if (sb == null) sb = new StringBuilder();
                sb.Append(stripped[index_stripped]);

                index_html++;
                index_stripped++;
            }
            else
            {
                if (sb != null)
                {
                    ParsedPuidsAndStrings.Add(sb.ToString());
                    sb = null;
                }

                while (index_html < html.Length && html[index_html] <= ' ') index_html++;
                
                if (!(index_html + "data-puid".Length < html.Length &&
                    html.Substring(index_html, "data-puid".Length) == "data-puid")) throw new Exception("Invalid HTML");
                
                index_html += "data-puid".Length;
                while (index_html < html.Length && html[index_html] != '"') index_html++;
                
                if (index_html >= html.Length) throw new Exception("Invalid HTML");
                
                string puid = html.Substring(index_html + 1, html.IndexOf('"', index_html + 1) - index_html - 1);
                index_html = html.IndexOf('"', index_html + 1)+1;
                
                ParsedPuidsAndStrings.Add(Guid.Parse(puid.Substring(1)));
            }
        }
        
        if (sb != null)
        {
            ParsedPuidsAndStrings.Add(sb.ToString());
            sb = null;
        }

        return ParsedPuidsAndStrings;
    }
}