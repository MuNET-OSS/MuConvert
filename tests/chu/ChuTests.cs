using System.Text;
using MuConvert.chu;
using MuConvert.utils;
using Rationals;

namespace MuConvert.Tests.chu;

public class ChuTests
{
    private static readonly Rational Tol768 = new(1, 768);
    private static readonly Rational Tol384 = new(1, 384);

    private static string TestsetDir => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "chu", "testset");
    private static string OfficialDir => Path.Combine(TestsetDir, "官谱");
    private static string CustomDir => Path.Combine(TestsetDir, "自制谱");

    public static IEnumerable<object[]> OfficialC2sChartPaths()
    {
        return Directory.EnumerateFiles(OfficialDir, "*.c2s", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(path => (object[])[Path.GetRelativePath(Environment.CurrentDirectory, path)]);
    }

    public static IEnumerable<object[]> CustomUgcChartPaths()
    {
        return Directory.EnumerateFiles(CustomDir, "*.ugc", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(path => (object[])[Path.GetRelativePath(Environment.CurrentDirectory, path)]);
    }

    [Theory]
    [MemberData(nameof(OfficialC2sChartPaths))]
    public void C2sRoundTrip(string c2sPath)
    {
        var (chart, _) = new C2sParser().Parse(File.ReadAllText(c2sPath));
        var (rt, _) = new C2sGenerator().Generate(chart);
        var (reparsed, _) = new C2sParser().Parse(rt);

        Assert.Equal(chart.Notes.Count, reparsed.Notes.Count);
        AssertNotesEqual(chart.Notes, reparsed.Notes);
    }

    private static void AssertNotesEqual(IReadOnlyList<ChuNote> expected_, IReadOnlyList<ChuNote> actual_, bool allowExDiff = false)
    {
        const string EOF = "<EOF>";
        List<ChuNote> expected = expected_.ToList();
        List<ChuNote> actual = actual_.ToList();
        
        for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            bool result;
            if (i >= expected.Count || i >= actual.Count) result = false;
            else 
            {
                result = CompareNote(expected[i], actual[i], allowExDiff);
                if (!result)
                {
                    // 尝试同一时刻的其他行有无相同的，如果有，交换之
                    var j = i + 1;
                    while (j < expected.Count && expected[j].Time == actual[i].Time)
                    {
                        if (CompareNote(expected[j], actual[i], allowExDiff))
                        {
                            (expected[j], expected[i]) = (expected[i], expected[j]);
                            result = true;
                            break;
                        }
                        j++;
                    }
                }
            }

            if (!result) {
                Assert.Fail(
                    $"Note mismatch at index {i}:{Environment.NewLine}" +
                    $"EXPECTED: {(i < expected.Count ? FormatNote(expected[i]) : EOF)}{Environment.NewLine}" +
                    $"ACTUAL  : {(i < actual.Count ? FormatNote(actual[i]) : EOF)}");
            }
        }
    }

    /// <summary>
    /// 比较两个音符是否实质等同；时间与时长等字段可命中宽容规则（见测试类内常量与分支注释）。
    /// </summary>
    public static bool CompareNote(ChuNote expected, ChuNote actual, bool allowExDiff = false)
    {
        if (!TypesEquivalent(expected.Type, actual.Type, allowExDiff)) return false;
        if (!TimesEquivalent(expected.Time, actual.Time)) return false;
        if (!DurationsEquivalent(expected, actual)) return false;
        if (expected.Cell != actual.Cell || expected.Width != actual.Width) return false;
        if (expected.EndCell != actual.EndCell || expected.EndWidth != actual.EndWidth) return false;
        if (Math.Abs(expected.Height - actual.Height) > 0.05m || Math.Abs(expected.EndHeight - actual.EndHeight) > 0.05m) return false;
        if (!TimesEquivalent(expected.CrushInterval, actual.CrushInterval)) return false;
        if (!TagsEquivalent(expected, actual)) return false;
        if (!TypesEquivalent(expected.TargetNote, actual.TargetNote, allowExDiff)) return false;
        return true;
    }

    /// <summary>规则 (a)：time 相差 ≤ 1/768 视为相等。</summary>
    private static bool TimesEquivalent(Rational a, Rational b) => (a - b).Abs() <= Tol768;

    /// <summary>
    /// 类型比较。<paramref name="allowExDiff"/> 为 true 时，HLD/HXD、SLD/SXD、SLC/SXC 之间允许互相匹配
    /// （即忽略 Ex 标志位差异）；否则要求严格相等。
    /// </summary>
    private static bool TypesEquivalent(string e, string a, bool allowExDiff)
    {
        if (e == a) return true;
        if (!allowExDiff) return false;
        return StripExFlag(e) == StripExFlag(a);

        static string StripExFlag(string t) => t switch
        {
            "HXD" => "HLD",
            "SXD" => "SLD",
            "SXC" => "SLC",
            "AHX" => "AHD",
            _ => t,
        };
    }

    /// <summary>
    /// 规则 (b)：|Δduration| ≤ 1/768，或（|Δduration| ≤ 1/384 且 |ΔendTime| ≤ 1/768）时视为 duration 语义相等。
    /// </summary>
    private static bool DurationsEquivalent(ChuNote e, ChuNote a)
    {
        var dd = (e.Duration - a.Duration).Abs().CanonicalForm;
        return dd <= Tol768 || (dd <= Tol384 && (e.EndTime - a.EndTime).Abs() <= Tol768);
    }

    /// <summary>规则 (c)(d)：广义 Air 的 DEF/空串；FLK 的 A/L。</summary>
    private static bool TagsEquivalent(ChuNote e, ChuNote a)
    {
        if (e.Tag == a.Tag) return true;
        if (e.Type == "ALD") return true; // C2S的ALD行，根据观测，是不支持颜色tag的。因此不要比较
        if (ChuUtils.IsGeneralizedAir(e))
        {
            if ((e.Tag == "DEF" && a.Tag == "") || (e.Tag == "" && a.Tag == "DEF"))
                return true;
        }
        if (e.Type == "FLK")
        {
            if ((e.Tag == "A" && a.Tag == "L") || (e.Tag == "L" && a.Tag == "A"))
                return true;
        }
        return false;
    }
    
    private static string FormatNote(ChuNote n) =>
        $"{n.Type} t={n.Time} start=({n.Cell},{n.Width}) dur={n.Duration} end=({n.EndCell},{n.EndWidth}) " +
        $"tag={n.Tag} tgt={n.TargetNote} h=({n.Height},{n.EndHeight}) crush={n.CrushInterval}";

    /// <summary>
    /// 比较两份 C2S 文本：各行按字典序排序后逐行匹配（允许原始行序不同）。
    /// </summary>
    private static void AssertC2sTextEqual(string expected, string actual)
    {
        var expectedLines = SplitC2sLines(expected);
        var actualLines = SplitC2sLines(actual);
        AssertSortedC2sLinesEqual(expectedLines, actualLines);
    }

    private static void AssertSortedC2sLinesEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        const string EOF = "<EOF>";
        const string label = "C2S";
        for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            if (i < expected.Count && i < actual.Count && C2sLinesEquivalent(expected[i], actual[i])) continue;
            Assert.Fail(
                $"{label} mismatch at sorted index {i}:{Environment.NewLine}" +
                $"EXPECTED: {(i < expected.Count ? expected[i] : EOF)}{Environment.NewLine}" +
                $"ACTUAL  : {(i < actual.Count ? actual[i] : EOF)}");
        }
    }

    /// <summary>除 ALD interval 的 `$` 宽松规则外，要求整行一致。</summary>
    private static bool C2sLinesEquivalent(string expected, string actual)
    {
        if (expected == actual) return true;
        if (!TryParseAldFields(expected, out var e) || !TryParseAldFields(actual, out var a)) return false;

        for (var i = 0; i < e.Length; i++)
        {
            if (i == 5) continue;
            if (e[i] != a[i]) return false;
        }

        if (!int.TryParse(e[7], out var durTicks)) return false;
        return AldIntervalsEquivalent(e[5], a[5], durTicks);
    }

    private static bool TryParseAldFields(string line, out string[] fields)
    {
        fields = line.Split('\t');
        return fields.Length >= 8
            && fields[0] == "ALD"
            && int.TryParse(fields[5], out _)
            && int.TryParse(fields[7], out _);
    }

    /// <summary>UGC `$` 在 C2S 中编码为 38400；此时另一边 interval 只需大于持续时长。</summary>
    private static bool AldIntervalsEquivalent(string intervalA, string intervalB, int durTicks)
    {
        if (intervalA == intervalB) return true;
        if (!int.TryParse(intervalA, out var a) || !int.TryParse(intervalB, out var b)) return false;

        if (a == 38400) return b > durTicks || (b == durTicks && b == 0);
        if (b == 38400) return a > durTicks || (a == durTicks && a == 0);
        return false;
    }

    /// <summary>
    /// 比较两份 UGC 文本：每个主行及其跟随行组成一个条目，条目按主行字典序排序后逐条匹配（允许条目顺序不同）。
    /// </summary>
    private static void AssertUgcTextEqual(string expected, string actual)
    {
        var expectedEntries = SplitUgcEntries(expected);
        var actualEntries = SplitUgcEntries(actual);
        AssertSortedLinesEqual(expectedEntries, actualEntries, "UGC");
    }

    private static List<string> SplitC2sLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.StartsWith("VERSION\t", StringComparison.Ordinal))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

    private static List<string> SplitUgcEntries(string text)
    {
        var entries = new List<string>();
        StringBuilder? current = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('\'') || line.StartsWith('@'))
                continue;

            if (IsUgcMainLine(line))
            {
                if (current != null) entries.Add(current.ToString());
                current = new StringBuilder(line);
            }
            else if (current != null)
            {
                current.Append('\n').Append(line);
            }
        }

        if (current != null) entries.Add(current.ToString());
        return entries.OrderBy(entry => entry, StringComparer.Ordinal).ToList();
    }

    private static bool IsUgcMainLine(string line)
    {
        if (!line.StartsWith('#')) return false;
        var colonIdx = line.IndexOf(':');
        if (colonIdx < 0) return false;
        return line[..colonIdx].Contains('\'');
    }

    private static void AssertSortedLinesEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string label)
    {
        const string EOF = "<EOF>";
        for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            if (i < expected.Count && i < actual.Count && expected[i] == actual[i]) continue;
            Assert.Fail(
                $"{label} mismatch at sorted index {i}:{Environment.NewLine}" +
                $"EXPECTED: {(i < expected.Count ? expected[i] : EOF)}{Environment.NewLine}" +
                $"ACTUAL  : {(i < actual.Count ? actual[i] : EOF)}");
        }
    }

    [Theory]
    [MemberData(nameof(CustomUgcChartPaths))]
    public void UgcToC2sViaGenerator(string ugcPath)
    {
        var (ugc, _) = new UgcParser().Parse(File.ReadAllText(ugcPath));
        Assert.NotEmpty(ugc.Notes);

        var (c2sText, _) = new C2sGenerator().Generate(ugc);
        Assert.Contains("VERSION", c2sText);
        Assert.Contains("TAP\t", c2sText);

        // 再把转出来的c2s，parse回去，比较是否和一开始的ugc等价（注意不是文本 round-trip，而是 IR 等价，允许字段重排但不允许信息丢失）
        var (c2sReparsed, _) = new C2sParser().Parse(c2sText);
        Assert.NotEmpty(c2sReparsed.Notes);
        AssertNotesEqual(ugc.Notes.Where(n => n.Type != "CLICK").ToList(), c2sReparsed.Notes);

        // 如果同目录下有 ground truth 的 c2s 文件，则再和 ground truth 比较一遍
        var groundTruthC2sPath = Directory.EnumerateFiles(Path.GetDirectoryName(ugcPath)!, "*.c2s")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (groundTruthC2sPath is not null)
        {
            AssertC2sTextEqual(File.ReadAllText(groundTruthC2sPath), c2sText);
        }
    }

    [Theory]
    [MemberData(nameof(OfficialC2sChartPaths))]
    public void C2sToUgcViaGenerator(string c2sPath)
    {
        var (c2s, _) = new C2sParser().Parse(File.ReadAllText(c2sPath));
        Assert.NotEmpty(c2s.Notes);
        
        var (ugcText, _) = new UgcGenerator().Generate(c2s);
        Assert.Contains("@VER", ugcText);
        Assert.Contains("#5'0", ugcText);

        // 再把转出来的ugc，parse回去，比较是否和一开始的c2s等价
        var (ugcReparsed, _) = new UgcParser().Parse(ugcText);
        Assert.NotEmpty(ugcReparsed.Notes);
        AssertNotesEqual(c2s.Notes, ugcReparsed.Notes.Where(n => n.Type != "CLICK").ToList(), allowExDiff: true);

        // 如果同目录下有 ground truth 的 ugc 文件，则再和 ground truth 比较一遍
        var groundTruthUgcPath = Directory.EnumerateFiles(Path.GetDirectoryName(c2sPath)!, "*.ugc")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (groundTruthUgcPath is not null)
        {
            AssertUgcTextEqual(File.ReadAllText(groundTruthUgcPath), ugcText);
        }
    }
}
