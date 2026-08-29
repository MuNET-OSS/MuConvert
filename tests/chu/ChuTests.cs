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
        if (!TagsEquivalent(expected, actual, allowExDiff)) return false;
        if (!TargetNotesEquivalent(expected, actual, allowExDiff)) return false;
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

    /// <summary>规则 (c)(d)：广义 Air 的 DEF/空串；FLK 的 A/L；allowExDiff 时非 Ex 音符可无 tag。</summary>
    private static bool TagsEquivalent(ChuNote e, ChuNote a, bool allowExDiff = false)
    {
        if (e.Tag == a.Tag) return true;
        if (e.Type == "ALD") return true; // C2S的ALD行，根据观测，是不支持颜色tag的。因此不要比较
        if (allowExDiff && TypesEquivalent(e.Type, a.Type, allowExDiff: true) && e.Type != a.Type)
        {
            var ex = IsExType(e.Type) ? e : IsExType(a.Type) ? a : null;
            var nonEx = IsExType(e.Type) ? a : IsExType(a.Type) ? e : null;
            if (ex is not null && nonEx is not null && nonEx.Tag == "") return true;
        }
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

    private static bool IsExType(string t) => t is "HXD" or "SXD" or "SXC" or "AHX";

    /// <summary>SLC/SLD 的 TargetNote 可有可无（新旧 C2S 版本差异）；ALD 的 TargetNote 由 C2S interval 推断，UGC 侧常无对应 previous。</summary>
    private static bool TargetNotesEquivalent(ChuNote e, ChuNote a, bool allowExDiff)
    {
        if (e.Type == "ALD" || a.Type == "ALD") return true;
        if (TypesEquivalent(e.TargetNote, a.TargetNote, allowExDiff)) return true;
        if (ChuUtils.IsSlide(e.Type) && ChuUtils.IsSlide(a.Type))
        {
            var et = e.TargetNote is "" or "N" ? "SLD" : e.TargetNote;
            var at = a.TargetNote is "" or "N" ? "SLD" : a.TargetNote;
            if (TypesEquivalent(et, at, allowExDiff)) return true;
        }
        return false;
    }
    
    private static string FormatNote(ChuNote n) =>
        $"{n.Type} t={n.Time} start=({n.Cell},{n.Width}) dur={n.Duration} end=({n.EndCell},{n.EndWidth}) " +
        $"tag={n.Tag} tgt={n.TargetNote} h=({n.Height},{n.EndHeight}) crush={n.CrushInterval}";

    /// <summary>
    /// 比较两份 C2S 文本：忽略头部元信息（TUTORIAL 及之前），各行按字典序排序后逐行匹配（允许原始行序不同）。
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

    /// <summary>除 ALD interval 的 `$` 宽松规则、ALD durationTicks ±1、ALD Height/EndHeight ±0.1、HLD/SLC/SLD 可选后缀外，要求整行一致。</summary>
    private static bool C2sLinesEquivalent(string expected, string actual)
    {
        if (expected == actual) return true;
        if (HoldSlideC2sLinesEquivalent(expected, actual)) return true;
        if (!TryParseAldFields(expected, out var e) || !TryParseAldFields(actual, out var a)) return false;

        for (var i = 0; i < e.Length; i++)
        {
            if (i is 5 or 7) continue;
            if (i is 6 or 10)
            {
                if (!decimal.TryParse(e[i], out var hE) || !decimal.TryParse(a[i], out var hA)) return false;
                if (Math.Abs(hE - hA) > 0.1m) return false;
                continue;
            }
            if (e[i] != a[i]) return false;
        }

        if (!int.TryParse(e[7], out var durE) || !int.TryParse(a[7], out var durA)) return false;
        if (!DurationTicksEquivalent(durE, durA)) return false;
        return AldIntervalsEquivalent(e[5], a[5], durE, durA);
    }

    private static readonly HashSet<string> C2sDirectionTags = ChuUtils.C2U_ChrExtras.Keys.ToHashSet();

    /// <summary>HLD/SLC/SLD：可选 TargetNote（SLD）；末尾方向标识符（见 ChuUtils.U2C_ChrExtras）任一侧可省略，两侧都有时必须一致。</summary>
    private static bool HoldSlideC2sLinesEquivalent(string expected, string actual)
    {
        var e = expected.Split('\t');
        var a = actual.Split('\t');
        if (e.Length == 0 || a.Length == 0 || e[0] != a[0]) return false;
        if (e[0] is not ("HLD" or "SLC" or "SLD")) return false;

        e = StripOptionalSlideTargetNote(e);
        a = StripOptionalSlideTargetNote(a);

        var eDir = e.Length > 0 && C2sDirectionTags.Contains(e[^1]) ? e[^1] : null;
        var aDir = a.Length > 0 && C2sDirectionTags.Contains(a[^1]) ? a[^1] : null;
        if (eDir is not null && aDir is not null && eDir != aDir) return false;

        e = StripOptionalDirectionTag(e);
        a = StripOptionalDirectionTag(a);

        e = StripOptionalSlideTargetNote(e);
        a = StripOptionalSlideTargetNote(a);

        return e.SequenceEqual(a);
    }

    private static string[] StripOptionalDirectionTag(string[] f) =>
        f.Length > 0 && C2sDirectionTags.Contains(f[^1]) ? f[..^1] : f;

    private static string[] StripOptionalSlideTargetNote(string[] f) =>
        f[0] is "SLC" or "SLD" && f.Length > 8 && f[^1] == "SLD" ? f[..^1] : f;

    private static bool TryParseAldFields(string line, out string[] fields)
    {
        fields = line.Split('\t');
        return fields.Length >= 8
            && fields[0] == "ALD"
            && int.TryParse(fields[5], out _)
            && int.TryParse(fields[7], out _);
    }

    /// <summary>ALD durationTicks 在 `$`/0 interval 编码切换时可能相差 1。</summary>
    private static bool DurationTicksEquivalent(int a, int b) => Math.Abs(a - b) <= 1;

    /// <summary>UGC `$` 在 C2S 中编码为 38400；此时另一边 interval 不管是什么都可以。</summary>
    private static bool AldIntervalsEquivalent(string intervalA, string intervalB, int durTicksA, int durTicksB)
    {
        if (intervalA == intervalB) return true;
        if (!int.TryParse(intervalA, out var a) || !int.TryParse(intervalB, out var b)) return false;

        if (a == 38400) return true; // b > durTicksB || (b == durTicksB && b == 0);
        if (b == 38400) return true; // a > durTicksA || (a == durTicksA && a == 0);
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

    private static readonly HashSet<string> C2sHeaderTags = new(StringComparer.Ordinal)
    {
        "VERSION", "MUSIC", "SEQUENCEID", "DIFFICULT", "LEVEL", "CREATOR",
        "BPM_DEF", "MET_DEF", "RESOLUTION", "CLK_DEF", "PROGJUDGE_BPM", "PROGJUDGE_AER", "TUTORIAL", "GENERATED_BY",
    };

    private static bool IsC2sHeaderLine(string line)
    {
        var tab = line.IndexOf('\t');
        return C2sHeaderTags.Contains(tab >= 0 ? line[..tab] : line);
    }

    private static List<string> SplitC2sLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !IsC2sHeaderLine(line))
            .OrderBy(AldAwareC2sSortKey, StringComparer.Ordinal)
            .ToList();

    /// <summary>ALD 行排序时忽略 interval（字段 5）与 durationTicks（字段 7）。</summary>
    private static string AldAwareC2sSortKey(string line)
    {
        if (!line.StartsWith("ALD\t")) return line;
        var fields = line.Split('\t');
        return fields.Length <= 7 ? line : string.Join('\t', fields.Where((_, i) => i is not (5 or 7)));
    }

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
