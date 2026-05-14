using System.Text.RegularExpressions;

namespace EmailArchiveViewer;

/// <summary>Crude HTML-to-plain-text for the "pretty much ASCII" body view. Shared by both
/// format facades.</summary>
public static partial class HtmlText
{
    public static string ToText(string html)
    {
        string s = StyleAndScript().Replace(html, " ");
        s = BreakTags().Replace(s, "\n");
        s = TagStrip().Replace(s, "");
        s = System.Net.WebUtility.HtmlDecode(s);
        s = ManyBlankLines().Replace(s, "\n\n");
        return s.Trim();
    }

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleAndScript();

    [GeneratedRegex(@"<br\s*/?>|</p>|</div>|</tr>|</td>|</th>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagStrip();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBlankLines();
}
