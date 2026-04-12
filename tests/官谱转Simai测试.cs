using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MuConvert.generator;
using MuConvert.maidata;
using MuConvert.parser;
using Rationals;
using Xunit.Abstractions;
using static MuConvert.Tests.TestUtils;

namespace MuConvert.Tests;

/// <summary>
/// 官谱 MA2 → Simai 与 maidata 中 inote 的比对：不逐字对比 Simai 文本，而是把双方按「顶层逗号」展开为
/// (Rational 时刻, 片段原文)，再比较时间轴（容忍分音写法不同导致的空白差异，以及少量 modifier 顺序差异）。
/// </summary>
public class 官谱转Simai测试
{
    private readonly ITestOutputHelper _output;

    public 官谱转Simai测试(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> AllLevels()
    {
        var repoRoot = FindRepoRoot();
        var testsetRoot = Path.Combine(repoRoot.FullName, "tests", "testset", "官谱");
        if (!Directory.Exists(testsetRoot))
            throw new DirectoryNotFoundException($"Testset root not found: {testsetRoot}");

        foreach (var maidataPath in Directory.EnumerateFiles(testsetRoot, "maidata.txt", SearchOption.AllDirectories))
        {
            var maidataTxt = File.ReadAllText(maidataPath, Encoding.UTF8);
            var maidata = new Maidata(maidataTxt);
            foreach (var id in maidata.Levels.Keys.OrderBy(k => k))
                yield return [new TestInput(maidataPath, id)];
        }
    }

    [Theory]
    [MemberData(nameof(AllLevels))]
    public void TestChart(TestInput c)
    {
        var maidata = new Maidata(File.ReadAllText(c.Maidata, Encoding.UTF8));
        var inote = maidata.Levels[c.LevelId].Inote;
        var ma2Text = File.ReadAllText(c.MA2, Encoding.UTF8);
        
        var (chart, _) = new MA2Parser().Parse(ma2Text);
        var (simai, _) = new SimaiGenerator().Generate(chart);

        var expectedTimeline = SimaiCommaTimeline.Flatten(inote);
        var actualTimeline = SimaiCommaTimeline.Flatten(simai);
        SimaiCommaTimeline.AssertTimelineEqual(expectedTimeline, actualTimeline);
    }
}

/// <summary>
/// 按 Simai 文法 <c>chart: (notations ',')*</c> 将谱面切成顶层逗号分段，并在每个分段上复现与
/// <see cref="MuConvert.parser.simai.SimaiParser"/> 一致的 <c>now</c> / <c>step</c> 推进规则，
/// 得到 (时刻, 原文) 序列；不构造 Note，不把片段再交给 SimaiParser。
/// </summary>
internal static class SimaiCommaTimeline
{
    /// <summary>与谱面语义相关的条目：BPM 标记、音符/休止以外的 met 变更等只影响 step，不单独出条。</summary>
    public readonly record struct Entry(Rational Time, string Text);

    public static List<Entry> Flatten(string simai)
    {
        var parts = simai.Split(',').Select(x => x.Trim()).ToList();
        if (parts.Last() == "E") parts.RemoveAt(parts.Count - 1);
        var now = new Rational(0);
        var step = new Rational(1, 4);
        var currentBpm = 60m;
        decimal? absStepSeconds = null;
        var list = new List<Entry>();

        foreach (var part in parts)
        {
            if (part.Length > 0)
            {
                ParseNotationsSegment(part.AsSpan(), now, ref currentBpm, ref step, ref absStepSeconds, list);
            }
            now = (now + step).CanonicalForm;
        }

        return list;
    }

    public static void AssertTimelineEqual(IReadOnlyList<Entry> expected, IReadOnlyList<Entry> actual)
    {
        static IEnumerable<(Rational t, string canon)> Canon(IReadOnlyList<Entry> e) =>
            e.Select(x => (t: x.Time, canon: NormalizeForCompare(x.Text)))
                .OrderBy(p => p.t)
                .ThenBy(p => p.canon, StringComparer.Ordinal);

        var a = Canon(expected).ToList();
        var b = Canon(actual).ToList();
        if (a.Count != b.Count)
            throw new Xunit.Sdk.XunitException(
                $"Timeline count differs: expected {a.Count}, actual {b.Count}.{FormatTailDiff(a, b)}");

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].t != b[i].t || !string.Equals(a[i].canon, b[i].canon, StringComparison.Ordinal))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"First mismatch at index {i}:");
                var exRaw = expected.FirstOrDefault(e =>
                    NormalizeForCompare(e.Text) == a[i].canon && e.Time == a[i].t).Text;
                var acRaw = actual.FirstOrDefault(e =>
                    NormalizeForCompare(e.Text) == b[i].canon && e.Time == b[i].t).Text;
                sb.AppendLine($"  expected: ({a[i].t}) {exRaw ?? "<none>"}");
                sb.AppendLine($"  actual  : ({b[i].t}) {acRaw ?? "<none>"}");
                sb.AppendLine("  (normalized strings follow)");
                sb.AppendLine($"  expected≈: {a[i].t} | {a[i].canon}");
                sb.AppendLine($"  actual≈  : {b[i].t} | {b[i].canon}");
                sb.AppendLine(FormatNeighborhood(a, b, i));
                throw new Xunit.Sdk.XunitException(sb.ToString());
            }
        }
    }

    private static string FormatNeighborhood(List<(Rational t, string canon)> a, List<(Rational t, string canon)> b, int i)
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- context (expected) ---");
        for (var j = Math.Max(0, i - 3); j < Math.Min(a.Count, i + 5); j++)
            sb.AppendLine($"  [{j}] {a[j].t} | {a[j].canon}");
        sb.AppendLine("--- context (actual) ---");
        for (var j = Math.Max(0, i - 3); j < Math.Min(b.Count, i + 5); j++)
            sb.AppendLine($"  [{j}] {b[j].t} | {b[j].canon}");
        return sb.ToString();
    }

    private static string FormatTailDiff(List<(Rational t, string canon)> a, List<(Rational t, string canon)> b)
    {
        var sb = new StringBuilder();
        var n = Math.Min(8, Math.Max(a.Count, b.Count));
        sb.AppendLine("--- tail expected ---");
        foreach (var x in a.TakeLast(Math.Min(n, a.Count)))
            sb.AppendLine($"  {x.t} | {x.canon}");
        sb.AppendLine("--- tail actual ---");
        foreach (var x in b.TakeLast(Math.Min(n, b.Count)))
            sb.AppendLine($"  {x.t} | {x.canon}");
        return sb.ToString();
    }

    /// <summary>
    /// 去掉空白、统一 b/x/f 连续修饰符的字典序，便于与「别的转谱器」对照。
    /// </summary>
    internal static string NormalizeForCompare(string s)
    {
        s = s.Trim().Replace("\r", "").Replace("\n", "");
        s = Regex.Replace(s, @"\s+", "");
        s = Regex.Replace(s, "[bxf]{2,}", m => new string(m.Value.OrderBy(c => c).ToArray()));
        return s;
    }

    private static void ParseNotationsSegment(
        ReadOnlySpan<char> span,
        Rational now,
        ref decimal currentBpm,
        ref Rational step,
        ref decimal? absStepSeconds,
        List<Entry> list)
    {
        var i = 0;
        while (i < span.Length)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i]))
                i++;
            if (i >= span.Length)
                break;

            if (span[i] == '(')
            {
                var close = span[i..].IndexOf(')');
                if (close < 0)
                    throw new InvalidOperationException("Unclosed '(' in simai segment: " + span.ToString());
                close += i;
                var inner = span[(i + 1)..close];
                if (!decimal.TryParse(inner.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var bpm))
                    throw new InvalidOperationException("Bad BPM: " + inner.ToString());
                list.Add(new Entry(now, $"({inner})"));
                currentBpm = bpm;
                if (absStepSeconds is { } abs)
                    step = (Rational)abs / (240 / (Rational)currentBpm);
                i = close + 1;
                continue;
            }

            if (span[i] == '{')
            {
                var close = FindClosingBrace(span, i);
                var inner = span[(i + 1)..close];
                i = close + 1;
                if (inner.Length > 0 && inner[0] == '#')
                {
                    var num = inner[1..].Trim();
                    if (!decimal.TryParse(num, NumberStyles.Number, CultureInfo.InvariantCulture, out var sec))
                        throw new InvalidOperationException("Bad absolute step: " + inner.ToString());
                    absStepSeconds = sec;
                    step = (Rational)absStepSeconds.Value / (240 / (Rational)currentBpm);
                }
                else
                {
                    absStepSeconds = null;
                    if (!int.TryParse(inner.Trim(), out var quaver) || quaver <= 0)
                        throw new InvalidOperationException("Bad met: {" + inner.ToString() + "}");
                    step = new Rational(1, quaver);
                }
                continue;
            }

            var bracket = 0;
            var start = i;
            while (i < span.Length)
            {
                var c = span[i];
                if (c == '[') bracket++;
                else if (c == ']' && bracket > 0) bracket--;
                if (bracket == 0 && (c == '(' || c == '{'))
                    break;
                i++;
            }

            var noteSpan = span[start..i].Trim();
            if (noteSpan.Length > 0)
                list.Add(new Entry(now, noteSpan.ToString()));
        }
    }

    private static int FindClosingBrace(ReadOnlySpan<char> span, int openIdx)
    {
        var d = 0;
        for (var j = openIdx; j < span.Length; j++)
        {
            if (span[j] == '{') d++;
            else if (span[j] == '}')
            {
                d--;
                if (d == 0) return j;
            }
        }
        throw new InvalidOperationException("Unclosed '{' in simai segment.");
    }
}
