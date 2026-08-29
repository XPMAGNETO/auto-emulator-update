using System.Text.RegularExpressions;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class VersionService
{
    private static readonly Regex SemVer = new(@"(?<!\d)(\d+)\.(\d+)(?:\.(\d+))?(?:[-+.]([0-9A-Za-z.-]+))?", RegexOptions.Compiled);
    private static readonly Regex DolphinCalendar = new(@"^(?<base>\d{4})(?<letter>[a-z])?(?:-(?<dev>\d+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Compare(string? current, string? latest)
    {
        if (string.Equals(current, latest, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.IsNullOrWhiteSpace(current)) return -1;
        if (string.IsNullOrWhiteSpace(latest)) return 1;

        var c = current.Trim().TrimStart('v', 'V');
        var l = latest.Trim().TrimStart('v', 'V');

        var cm = DolphinCalendar.Match(c);
        var lm = DolphinCalendar.Match(l);
        if (cm.Success && lm.Success)
        {
            var cb = int.Parse(cm.Groups["base"].Value);
            var lb = int.Parse(lm.Groups["base"].Value);
            if (cb != lb) return cb.CompareTo(lb);
            var cl = cm.Groups["letter"].Success ? cm.Groups["letter"].Value[0] - 'a' + 1 : 0;
            var ll = lm.Groups["letter"].Success ? lm.Groups["letter"].Value[0] - 'a' + 1 : 0;
            if (cl != ll) return cl.CompareTo(ll);
            var cd = cm.Groups["dev"].Success ? int.Parse(cm.Groups["dev"].Value) : 0;
            var ld = lm.Groups["dev"].Success ? int.Parse(lm.Groups["dev"].Value) : 0;
            return cd.CompareTo(ld);
        }

        var cs = ParseSemVer(c);
        var ls = ParseSemVer(l);
        if (cs is not null && ls is not null) return cs.CompareTo(ls);

        return StringComparer.OrdinalIgnoreCase.Compare(c, l);
    }

    public string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var cal = Regex.Match(text, @"\b\d{4}[a-z]?(?:-\d+)?\b", RegexOptions.IgnoreCase);
        if (cal.Success) return cal.Value;
        var m = SemVer.Match(text);
        return m.Success ? m.Value : null;
    }

    private static Version? ParseSemVer(string s)
    {
        var m = SemVer.Match(s);
        if (!m.Success) return null;
        return new Version(
            int.Parse(m.Groups[1].Value),
            int.Parse(m.Groups[2].Value),
            m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }
}
