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

    private static void AssertNotesEqual(IReadOnlyList<ChuNote> expected_, IReadOnlyList<ChuNote> actual_)
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
                result = CompareNote(expected[i], actual[i]);
                if (!result)
                {
                    // 尝试同一时刻的其他行有无相同的，如果有，交换之
                    var j = i + 1;
                    while (j < expected.Count && expected[j].Time == actual[i].Time)
                    {
                        if (CompareNote(expected[j], actual[i]))
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
    public static bool CompareNote(ChuNote expected, ChuNote actual)
    {
        if (expected.Type != actual.Type) return false;
        if (!TimesEquivalent(expected.Time, actual.Time)) return false;
        if (!DurationsEquivalent(expected, actual)) return false;
        if (expected.Cell != actual.Cell || expected.Width != actual.Width) return false;
        if (expected.EndCell != actual.EndCell || expected.EndWidth != actual.EndWidth) return false;
        if (Math.Abs(expected.Height - actual.Height) > 0.05m || Math.Abs(expected.EndHeight - actual.EndHeight) > 0.05m) return false;
        if (expected.CrushInterval != actual.CrushInterval) return false;
        if (!TagsEquivalent(expected, actual)) return false;
        if (expected.TargetNote != actual.TargetNote) return false;
        return true;
    }

    /// <summary>规则 (a)：time 相差 ≤ 1/768 视为相等。</summary>
    private static bool TimesEquivalent(Rational a, Rational b) => (a - b).Abs() <= Tol768;

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
        var (c2sChart, _) = new C2sParser().Parse(c2sText);
        Assert.NotEmpty(c2sChart.Notes);
        AssertNotesEqual(ugc.Notes.Where(n => n.Type != "CLICK").ToList(), c2sChart.Notes);
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
        AssertNotesEqual(c2s.Notes, ugcReparsed.Notes.Where(n => n.Type != "CLICK").ToList());
    }
}
