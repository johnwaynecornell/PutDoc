// Services/HtmlTransformService.cs

using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser; 
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop; // for .ToHtml()

namespace PutDoc.Services;
//pc06d9280230f4fe6b9480035d0726cd6
public static class HtmlTransformService
{
    static readonly HtmlParser FragParser = new HtmlParser();
    public static string SerializeFragment(INode root)
    {
        // Preserves text nodes, comments, and elements—plus their whitespace.
        return string.Concat(root.ChildNodes.Select(n => n.ToHtml()));
    }
    
    static string SerializeFragment(INode root, IMarkupFormatter fmt) =>
        string.Concat(root.ChildNodes.Select(n => n.ToHtml(fmt)));

    
    #region Beautifier
     // Tags where we NEVER collapse whitespace
    static readonly HashSet<string> PreserveWhitespaceTags =
        new(StringComparer.OrdinalIgnoreCase) { "pre", "code", "textarea", "script", "style" };

    static IElement? GetParentElement(INode node)
    {
        var p = node.Parent;
        while (p is not null && p is not IElement)
            p = p.Parent;
        return p as IElement;
    }
    static bool IsInPreserveContext(INode node)
    {
        var parent = GetParentElement(node);
        if (parent is null)
        {
            // handle root/no parent case
            return false;
        }
        for (var p = parent; p != null; p = p.ParentElement)
            if (PreserveWhitespaceTags.Contains(p.LocalName)) return true;
        return false;
    }

     static void NormalizeWhitespace(INode scope)
    {
        foreach (var text in scope.Descendents().OfType<IText>())
        {
            if (IsInPreserveContext(text)) continue;
            var s = text.TextContent;
            if (string.IsNullOrEmpty(s)) continue;

            // Collapse all whitespace runs to a single ASCII space
            text.TextContent = Regex.Replace(s, @"\s+", " ");
        }
    }
     
    static readonly HashSet<string> InlineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","abbr","b","bdi","bdo","br","cite","code","data","dfn","em","i","kbd",
        "mark","q","rp","rt","ruby","s","samp","small","span","strong","sub","sup",
        "time","u","var","wbr","del","ins","img","button","input","label","select"
        // textarea is inline-ish but in Preserve set; script/style too.
    };

    static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "address","article","aside","blockquote","div","dl","dt","dd","fieldset","figcaption","figure",
        "footer","form","h1","h2","h3","h4","h5","h6","header","hr","li","main","nav","noscript",
        "ol","p","pre","section","table","thead","tbody","tfoot","tr","td","th","ul"
    };

    static bool IsInlineNode(INode? n) =>
        n is IText || (n as IElement) is { } e && InlineTags.Contains(e.LocalName);

    static bool IsBlockNode(INode? n) =>
        (n as IElement) is { } e && BlockTags.Contains(e.LocalName);

    static bool IsBlockParent(INode? n) =>
        (n as IElement) is { } e && BlockTags.Contains(e.LocalName);

    static INode? GetPreviousSiblingNode(INode node)
    {
        var parent = node.Parent;
        if (parent is null) return null;
        var children = parent.ChildNodes;
        for (int i = 0; i < children.Length; i++)
        {
            if (ReferenceEquals(children[i], node))
            {
                return i > 0 ? children[i - 1] : null;
            }
        }

        return null;
    }

    static INode? GetNextSiblingNode(INode node)
    {
        var parent = node.Parent;
        if (parent is null) return null;
        var children = parent.ChildNodes;
        for (int i = 0; i < children.Length; i++)
        {
            if (ReferenceEquals(children[i], node))
            {
                return i + 1 < children.Length ? children[i + 1] : null;
            }
        }

        return null;
    }


    // Replaces your current CondenseWhitespace
    static void CondenseWhitespace(INode scope)
    {
        foreach (var t in scope.Descendents().OfType<IText>().ToList())
        {
            if (IsInPreserveContext(t)) continue;

            var s = t.TextContent ?? string.Empty;

            if (string.IsNullOrWhiteSpace(s))
            {
                // Whitespace-only text node
                var prev = GetPreviousSiblingNode(t);
                var next = GetNextSiblingNode(t);

                // If both neighbors are inline/text → keep ONE space to separate words.
                if (IsInlineNode(prev) && IsInlineNode(next))
                {
                    t.TextContent = " ";
                }
                else
                {
                    // Indentation or boundary whitespace near block nodes → remove entirely.
                    t.Remove();
                }

                continue;
            }

            // Collapse runs
            s = Regex.Replace(s, @"\s+", " ");

            // If inside a block parent, trim edges that touch non-inline boundaries
            var prevN = GetPreviousSiblingNode(t);
            var nextN = GetNextSiblingNode(t);

            if (IsBlockParent(t.Parent))
            {
                if (!IsInlineNode(prevN)) s = s.TrimStart();
                if (!IsInlineNode(nextN)) s = s.TrimEnd();
            }

            // If trimming collapsed everything and it's not between two inlines, drop it.
            if (s.Length == 0 && !(IsInlineNode(prevN) && IsInlineNode(nextN)))
            {
                t.Remove();
            }
            else
            {
                t.TextContent = s;
            }
        }
    }



    // Replace your existing CapturePreBlocks with this version
    sealed class PreShield { public string Id = default!; public string RawOuterHtml = default!; }

    static List<PreShield> CapturePreBlocks(IElement root)
    {
        var rawFmt = HtmlMarkupFormatter.Instance; // exact bytes
        var list = new List<PreShield>();

        foreach (var pre in root.QuerySelectorAll("pre").OfType<IElement>())
        {
            // 1) Determine id, but DO NOT serialize with the attribute on the element
            var hadAttr = pre.HasAttribute("data-preserve-pre");
            var oldId   = hadAttr ? pre.GetAttribute("data-preserve-pre") : null;
            var id      = oldId ?? ("prekeep_" + Guid.NewGuid().ToString("N"));

            // 2) Temporarily remove the attribute (if present), capture RAW without it
            if (hadAttr) pre.RemoveAttribute("data-preserve-pre");
            var raw = pre.ToHtml(rawFmt);              // <-- no data-preserve-pre in saved bytes
            // 3) Reapply our marker attribute for the serializer passthrough step
            pre.SetAttribute("data-preserve-pre", id); // ensure marker exists for traversal

            list.Add(new PreShield { Id = id, RawOuterHtml = raw });
        }

        return list;
    }

    static string SerializeFragmentWithPrePassthrough(INode root,
        IMarkupFormatter fmt, IReadOnlyDictionary<string,string> preMap)
    {
        var sb = new System.Text.StringBuilder();

        void WriteNode(INode n)
        {
            if (n is IElement el &&
                el.LocalName.Equals("pre", StringComparison.OrdinalIgnoreCase) &&
                el.HasAttribute("data-preserve-pre"))
            {
                var id = el.GetAttribute("data-preserve-pre")!;
                if (preMap.TryGetValue(id, out var raw))
                {
                    sb.Append(raw);        // emit captured raw HTML
                    return;                // IMPORTANT: skip walking children
                }
            }
            sb.Append(n.ToHtml(fmt));
        }

        foreach (var child in root.ChildNodes)
            WriteNode(child);

        var html = sb.ToString();
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"\s*data-preserve-pre=""[^""]*""",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return html;
    }
    // Add an optional knob; default = keep explicit end tags.
    public static async Task<string> CondenseAsync(string html, bool keepOptionalEndTags = true)
    {
        var doc  = await Ctx.OpenAsync(req => req.Content(html ?? string.Empty));
        var root = doc.Body ?? doc.DocumentElement;

        // Capture raw <pre> bytes but don't replace them
        var shields = CapturePreBlocks(root);
        var preMap  = shields.ToDictionary(s => s.Id, s => s.RawOuterHtml);

        // Block-aware, idempotent whitespace condensing (your current version)
        CondenseWhitespace(root);

        // Formatter choice:
        // - HtmlMarkupFormatter: emits explicit </p>, </li>, etc.
        // - MinifyMarkupFormatter: omits optional closing tags (more compact, but what you saw)
        IMarkupFormatter fmt = keepOptionalEndTags
            ? HtmlMarkupFormatter.Instance
            : new MinifyMarkupFormatter();

        // Single-pass serialization with <pre> passthrough
        return SerializeFragmentWithPrePassthrough(root, fmt, preMap);
    }

    public static async Task<string> BeautifyAsync(string html)
    {
        var doc  = await Ctx.OpenAsync(req => req.Content(html ?? string.Empty));
        var root = doc.Body ?? doc.DocumentElement;

        var shields = CapturePreBlocks(root);
        var preMap  = shields.ToDictionary(s => s.Id, s => s.RawOuterHtml);

        // Optional: collapse runs outside preserve contexts (keeps inline edge spaces)
        CondenseWhitespace(root);

        var pretty = new PrettyMarkupFormatter { Indentation = "  ", NewLine = "\n" };
        return SerializeFragmentWithPrePassthrough(root, pretty, preMap);
    }


    #endregion //Beautifier
    


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
                if (first)
                {
                    first = false;
                    continue;
                }

                el.SetAttribute("data-puid", "p" + Guid.NewGuid().ToString("N"));
            }
        }
    }

    public static async Task<string> FreshenPuids(string html)
    {
        var doc = await Ctx.OpenAsync(req => req.Content(html ?? ""));
        var root = doc.Body!;

        foreach (var el in root.QuerySelectorAll(HtmlPuid.query))
        {
            if (el.HasAttribute("data-puid")) el.RemoveAttribute("data-puid");
            EnsurePuid(el);
        }

        // 👇 was: root.OuterHtml
        return SerializeFragment(root); // emits body.childNodes joined, whitespace preserved
    }

    public static async Task<IElement> FetchTarget(PutDocState state, Guid snippetId, string puid)
    {
        var page = state.CurrentPage();
        if (page is null) return null;
        state.SelectSnippet(snippetId);
        var snip = page.Snippets.FirstOrDefault(s => s.Id == snippetId);
        if (snip is null) return null;

        var doc = await Ctx.OpenAsync(req => req.Content(snip.Html ?? ""));
        var root = doc.Body!;

        // Ensure actionable elements at least have *some* puid (for future)
        foreach (var el in root.QuerySelectorAll(HtmlPuid.query))
            EnsurePuid(el);

        var target = FindByPuid(root, puid);
        return target;
    }

    public static async Task InsertBeforeAsync(PutDocState state, Guid snippetId, string puid, string html)
    {
        var page = state.CurrentPage();
        if (page is null) return;
        state.SelectSnippet(snippetId);
        var snip = page.Snippets.FirstOrDefault(s => s.Id == snippetId);
        if (snip is null) return;

        var doc = await Ctx.OpenAsync(req => req.Content(snip.Html ?? ""));
        var root = doc.Body!;

        // Ensure actionable elements at least have *some* puid (for future)
        foreach (var el in root.QuerySelectorAll(HtmlPuid.query))
            EnsurePuid(el);

        var target = FindByPuid(root, puid);
        
        target.Insert(AdjacentPosition.BeforeBegin, html);
        
        foreach (var el in target.QuerySelectorAll(HtmlPuid.query))
            EnsurePuid(el);
        
        ReassignDuplicatePuids(root);
        var newHtml = SerializeFragment(root);
        if (!string.Equals(newHtml, snip.Html, StringComparison.Ordinal))
        {
            await state.SetSnippetHtml(newHtml, isRawFromEditor: false);
        }
        
        state.SelectSnippet(snippetId);
    }
    public static async Task<bool> ApplyAsync(PutDocState state, Guid snippetId, string action, string puid,
        string? path = null)
    {

        var page = state.CurrentPage();
        if (page is null) return false;
        state.SelectSnippet(snippetId);
        var snip = page.Snippets.FirstOrDefault(s => s.Id == snippetId);
        if (snip is null) return false;

        var doc = await Ctx.OpenAsync(req => req.Content(snip.Html ?? ""));
        var root = doc.Body!;

        // Ensure actionable elements at least have *some* puid (for future)
        foreach (var el in root.QuerySelectorAll(HtmlPuid.query))
            EnsurePuid(el);

        var target = FindByPuid(root, puid);
        
        if (target is null) return false;

        bool changed = false;

        switch (action)
        {
            case "clone":
                target.Insert(AdjacentPosition.AfterEnd, await FreshenPuids(target.OuterHtml));
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
                if (!string.Equals(justPersistPuids, snip.Html, StringComparison.Ordinal))
                {
                    await state.SetSnippetHtml(justPersistPuids);
                }

                state.SelectSnippet(snippetId);
            }

            return true;
        }

        // After structural changes, ensure uniqueness and persist
        ReassignDuplicatePuids(root);
        var newHtml = SerializeFragment(root);
        if (!string.Equals(newHtml, snip.Html, StringComparison.Ordinal))
        {
            await state.SetSnippetHtml(newHtml, isRawFromEditor: false);
        }
        
        state.SelectSnippet(snippetId);
        return true;
    }

    public static async Task<string?> ExtractFragmentInnerByPuidAsync(string html, string puid)
    {
        var doc = await Ctx.OpenAsync(req => req.Content(html));
        var el = doc.QuerySelector($"[data-puid=\"{puid}\"]") as IElement;
        return el?.InnerHtml;
    }

    public static async Task<string?> ExtractFragmentOuterByPuidAsync(string html, string puid)
    {
        var doc = await Ctx.OpenAsync(req => req.Content(html));
        var el = doc.QuerySelector($"[data-puid=\"{puid}\"]") as IElement;
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
        var el = doc.QuerySelector($"[data-puid=\"{puid}\"]") as IElement;
        if (el is null) return null;

        if (replaceOuter)
        {
            // Parse the replacement as a fragment in the *context* of the target element.
            // This ensures correct adoption (same document) and proper handling of void elements, etc.
            var nodes = FragParser.ParseFragment(replacement ?? string.Empty, el);

            var elementNodes = nodes.OfType<IElement>().ToList();
            var hasNonWhitespaceText = nodes.Any(n =>
                n.NodeType == NodeType.Text && !string.IsNullOrWhiteSpace(n.TextContent));

            if (elementNodes.Count == 1 && !hasNonWhitespaceText)
            {
                // Exactly one element, no stray text → safe outer replace
                var newEl = elementNodes[0];

                // Preserve/ensure the PUID
                if (!newEl.HasAttribute("data-puid"))
                    newEl.SetAttribute("data-puid", puid);

                el.Replace(newEl); // same-document node because of context parsing
            }
            else
            {
                // Multi-root or text present → keep the shell, replace inner
                el.InnerHtml = replacement ?? string.Empty;
            }
        }
        else
        {
            // Inner replace (simple path)
            el.InnerHtml = replacement ?? string.Empty;
        }

        var root = doc.Body ?? doc.DocumentElement;
        return SerializeFragment(root);
    }

}
