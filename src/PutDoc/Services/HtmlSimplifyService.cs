using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;

public static class HtmlSimplifier
{
    private const string SvgNs = "http://www.w3.org/2000/svg";

    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p","h1","h2","h3","h4","h5","h6",
        "ul","ol","li",
        "pre","code",
        "strong","em","b","i","u",
        "a",
        "table","thead","tbody","tr","th","td",
        "br",
        "img",
        "svg"
    };

    private static readonly HashSet<string> AllowedAttrs = new(StringComparer.OrdinalIgnoreCase)
    {
        "href","title","alt","src","colspan","rowspan"
    };

    public static async Task<string> SimplifyAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var context = BrowsingContext.New(Configuration.Default.WithDefaultLoader());

        var parser = new HtmlParser();
        var doc = await parser.ParseDocumentAsync(html);

        var body = doc.Body;
        if (body is null)
            return html;

        SimplifyNode(body);

        return body.InnerHtml.Trim();
    }

    private static void SimplifyNode(INode node)
    {
        var children = node.ChildNodes.ToArray();
        foreach (var child in children)
        {
            if (child.NodeType == NodeType.Text)
            {
                var text = child.TextContent;
                var collapsed = Regex.Replace(text, "\\s+", " ");
                child.TextContent = collapsed;
            }
            else if (child is IElement el)
            {
                SimplifyElement(el);
            }
        }
    }

    private static void SimplifyElement(IElement el)
    {
        // SVG: keep structure + attributes
        if (el.NamespaceUri == SvgNs /* or AngleSharp.Dom.NamespaceNames.Svg */)
        {
            SimplifyNode(el);
            return;
        }

        var tag = el.TagName.ToLowerInvariant();

        if (tag is "script" or "style")
        {
            el.Remove();
            return;
        }

        // Map div-ish tags to <p>
        if (tag is "div" or "section" or "article" or "main")
        {
            var doc = el.Owner;
            if (doc != null)
            {
                var p = doc.CreateElement("p");
                p.InnerHtml = el.InnerHtml;
                el.Replace(p);
                SimplifyNode(p);
            }
            return;
        }

        // If not allowed, unwrap into text (or children)
        if (!AllowedTags.Contains(tag))
        {
            var parent = el.Parent;
            var doc = parent?.Owner ?? el.Owner;
            if (parent != null && doc != null)
            {
                // Keep textual content as a text node using public factory
                var textNode = doc.CreateTextNode(el.TextContent ?? string.Empty);
                parent.ReplaceChild(doc, textNode, el);
            }
            else
            {
                el.Remove();
            }
            return;
        }

        // Clean attributes (non-SVG)
        foreach (var attr in el.Attributes.ToArray())
        {
            var name = attr.Name.ToLowerInvariant();

            if (name.StartsWith("on") ||
                name is "style" or "class" or "id" ||
                name.StartsWith("data-") ||
                name.StartsWith("aria-"))
            {
                el.RemoveAttribute(attr.Name);
                continue;
            }

            if (!AllowedAttrs.Contains(name))
            {
                el.RemoveAttribute(attr.Name);
                continue;
            }

            if (name == "href" && attr.Value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                el.RemoveAttribute(attr.Name);
            }
        }

        SimplifyNode(el);
    }

    private static void ReplaceChild(this INode parent, IDocument doc, INode newNode, INode oldNode)
    {
        if (parent == null || doc == null || newNode == null || oldNode == null)
            return;

        parent.InsertBefore(newNode, oldNode);
        oldNode.Parent?.RemoveChild(oldNode);
    }
}
