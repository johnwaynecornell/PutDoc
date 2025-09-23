using System.Text.RegularExpressions;

namespace PutDoc.Services;

public class AngleSoftFilter : IAngleSoftFilter
{
    // Remove <script>…</script> (any case, multiline)
    static readonly Regex ScriptTag =
        new("<script\\b.*?</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    // Remove inline event handlers like onclick=, onload=, etc.
    static readonly Regex OnAttr =
        new("\\son\\w+\\s*=\\s*(['\"]).*?\\1", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    // Remove ANY attribute that starts with '@' (e.g., @onclick, @bind, @on...),
    // which are Razor/Blazor directive attributes and invalid in plain HTML.
    static readonly Regex AtDirectiveAttr =
        new("\\s@[\\w:-]+\\s*=\\s*(['\"]).*?\\1", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public string Filter(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var cleaned = ScriptTag.Replace(html, "");
        cleaned = OnAttr.Replace(cleaned, "");
        cleaned = AtDirectiveAttr.Replace(cleaned, "");

        return cleaned;
    }
}