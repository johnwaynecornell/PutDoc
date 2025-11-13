using System.Diagnostics;

namespace PutDoc.Services;

public class HtmlText
{
    private static bool IsTagNameChar(char c) =>
        char.IsLetterOrDigit(c) || c == ':' || c == '-' || c == '_';

// Seek left to the opening '<' of the start tag containing position.
// Skips over quoted strings safely.
    private static int SeekLeftToStartTagOpen(string input, int position)
    {
        int i = Math.Max(0, Math.Min(position, input.Length - 1));
        bool inQuote = false;
        char quote = '\0';

        for (; i >= 0; i--)
        {
            char c = input[i];

            if (inQuote)
            {
                if (c == quote)
                {
                    // check for escaped quote like \" (very basic)
                    if (i > 0 && input[i - 1] == '\\') continue;
                    inQuote = false;
                    quote = '\0';
                }

                continue;
            }

            if (c == '"' || c == '\'')
            {
                inQuote = true;
                quote = c;
                continue;
            }

            if (c == '<')
            {
                // Verify it looks like a start tag, not a comment/doctype/close
                if (i + 1 < input.Length)
                {
                    char n = input[i + 1];
                    if (n == '/' || n == '!' || n == '?') continue; // not a start-tag open
                    // If next is a valid tag name start, accept
                    if (IsTagNameChar(n)) return i;
                    // Otherwise still accept as best effort
                    return i;
                }

                return i;
            }
        }

        return -1;
    }

// Seek right to just after the closing '>' for the start tag at or containing position.
// Skips quoted attributes and supports self-closing '/>'.
    private static int SeekRightPastStartTagClose(string input, int position)
    {
        int n = input.Length;
        int i = Math.Max(0, Math.Min(position, n));
        bool inQuote = false;
        char quote = '\0';

        for (; i < n; i++)
        {
            char c = input[i];

            if (inQuote)
            {
                if (c == quote)
                {
                    // Handle \" or \' escapes
                    if (i > 0 && input[i - 1] == '\\') continue;
                    inQuote = false;
                    quote = '\0';
                }

                continue;
            }

            if (c == '"' || c == '\'')
            {
                inQuote = true;
                quote = c;
                continue;
            }

            if (c == '>')
            {
                return i + 1; // position after '>'
            }

            // If we stumble into a '<' while scanning right, it likely means
            // we weren't actually in a start tag (malformed or different context).
            // Bail to avoid infinite scans.
            if (c == '<' && i != position)
            {
                break;
            }
        }

        return -1;
    }

// Public API:
// Given html, a caret position inside a start tag like <div data-puid="...">,
// and dir:
//  -1 → return index of '<'
//  +1 → return index just after '>'
    public static int SeekFromAttribute(string html, int position, int dir)
    {
        if (string.IsNullOrEmpty(html)) return -1;
        position = Math.Max(0, Math.Min(position, html.Length));

        // First, locate the opening '<' for the start tag containing position.
        // If the caret is before the '<', we’ll walk left safely through quotes.
        int lt = SeekLeftToStartTagOpen(html, position);
        if (lt < 0) return -1;

        if (dir < 0)
            return lt;

        // Walk right from max(position, lt) to find the closing '>' of that tag.
        int start = Math.Max(position, lt);
        int gtPlusOne = SeekRightPastStartTagClose(html, start);
        return gtPlusOne;
    }

    // public static int HtmlVarientsSync(string html_a, string html_b, int html_a_caret)
    // {
    //     int html_a_i = 0;
    //     int html_b_i = 0;
    //
    //     while (html_a_i < html_a.Length)
    //     {
    //         if (html_a[html_a_i] <= ' ')
    //         {
    //             while (html_b[html_b_i] <= ' ') html_b_i++;
    //             if (html_a_i++ == html_a_caret) 
    //                 return html_b_i;
    //             continue;
    //         } else if (html_a[html_a_i] == html_b[html_b_i])
    //         {
    //             html_b_i++;
    //             if (html_a_i++ == html_a_caret) 
    //                 return html_b_i;
    //
    //         }
    //         else if (html_b[html_b_i] <= ' ')
    //         {
    //             html_b_i++;
    //         } else
    //         {
    //             throw new Exception();
    //         }
    //     }
    //
    //     return html_b_i;
    // }
    
    /// <summary>
    /// Maps a caret position from string <paramref name="a"/> to its equivalent
    /// position in string <paramref name="b"/> when the two strings differ
    /// only by ASCII whitespace.  
    /// Returns <c>null</c> if a non-whitespace difference is found.
    /// </summary>
    public static int? WhitespaceVariantsSync(string a, string b, int aCaret)
    {
        int ai = 0, bi = 0;

        // Normalize caret bounds
        aCaret = Math.Clamp(aCaret, 0, a.Length);
        
        // Helper to test "ASCII whitespace" (matches your <= ' ' intent)
        static bool IsWs(char c) => c <= ' ';

        // Fast path: caret at start or end
        if (aCaret == 0) return 0;

        while (ai < a.Length || bi < b.Length)
        {
            // If we've arrived at the caret in A, return the current B position
            if (ai == aCaret)
            {
                while (bi < b.Length && IsWs(b[bi])) bi++;
                return bi;
            }

            // Case 1: A has whitespace here → advance A by one, and skip ALL whitespace in B
            if (ai < a.Length && IsWs(a[ai]))
            {
                ai++;
                while (bi < b.Length && IsWs(b[bi])) bi++;
                continue;
            }

            // Case 2: B has whitespace here (A does not) → skip B whitespace
            if (bi < b.Length && (ai >= a.Length || !IsWs(a[ai])) && IsWs(b[bi]))
            {
                bi++;
                continue;
            }

            // Case 3: Both have a character and they match → advance both
            if (ai < a.Length && bi < b.Length && a[ai] == b[bi])
            {
                ai++; bi++;
                continue;
            }

            // Case 4: One side is exhausted; allow trailing whitespace on the other
            if (ai < a.Length && bi >= b.Length && IsWs(a[ai])) { ai++; continue; }
            if (bi < b.Length && ai >= a.Length && IsWs(b[bi])) { bi++; continue; }

            // Otherwise there is a non-whitespace difference → not a "variants" match
            return null;
        }

        // If caret was exactly at a.Length, map to current B index
        return bi;
    }

}