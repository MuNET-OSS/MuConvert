using System.Text;
using MuConvert.chu;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.ChuUtils;

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

    [Fact]
    public void C2sMissingOptionalAttributesPreservesAirNoteParents()
    {
        var c2s = string.Join('\n',
            "BPM\t0\t0\t120",
            "CHR\t0\t0\t0\t4",
            "AHD\t0\t0\t0\t4\tCHR\t96",
            "SLD\t0\t0\t0\t4\t96\t6\t4\tSLD",
            "AHD\t0\t96\t6\t4\tSLD\t96");

        var (chart, alerts) = new C2sParser().Parse(c2s);

        Assert.Empty(alerts);
        var chr = Assert.Single(chart.Notes, n => n.Type == ChuNoteType.Tap);
        Assert.Equal(ExDirection.UP, chr.Ex);

        var slide = Assert.Single(chart.Notes, n => n.Type == ChuNoteType.Slide);
        var airHolds = chart.Notes.Where(n => n is { Type: ChuNoteType.Hold, IsAir: true }).ToList();
        Assert.Equal(2, airHolds.Count);
        Assert.Same(chr, airHolds.Single(n => n.TargetNote?.Type == ChuNoteType.Tap).TargetNote);
        Assert.Same(slide, airHolds.Single(n => n.TargetNote?.Type == ChuNoteType.Slide).TargetNote);
        Assert.All(airHolds, note => Assert.Equal(NoteColor.DEF, note.Color));
    }

    [Fact]
    public void C2sSlideMissingEndWidthContinuesAtStartWidth()
    {
        var c2s = string.Join('\n',
            "BPM\t0\t0\t120",
            "SLC\t0\t0\t6\t4\t24\t6",
            "SLC\t0\t24\t6\t4\t52\t5");

        var (chart, alerts) = new C2sParser().Parse(c2s);

        Assert.Empty(alerts);
        var slide = Assert.Single(chart.Notes);
        Assert.Equal(2, slide.Segments.Count);
        Assert.Equal(4, slide.Segments[0].EndWidth);
    }

    /// <summary>
    /// 绝对坐标下的一段 Slide/Hold/Crush 路径，用于跨 note 的 segment 错配回收。
    /// </summary>
    private readonly record struct AbsSeg(
        ChuNoteType Type,
        bool IsAir,
        bool C,
        Rational StartTime,
        int StartCell,
        int StartWidth,
        decimal StartHeight,
        Rational Length,
        int EndCell,
        int EndWidth,
        decimal EndHeight);

    private static void AssertNotesEqual(IReadOnlyList<ChuNote> expected_, IReadOnlyList<ChuNote> actual_, bool allowExDiff = false)
    {
        const string EOF = "<EOF>";
        var expected = expected_.ToList();
        var actual = actual_.ToList();
        var usedActual = new bool[actual.Count];
        var expectedSegPool = new List<AbsSeg>();
        var actualSegPool = new List<AbsSeg>();
        string? deferredFail = null;

        for (var i = 0; i < expected.Count; i++)
        {
            // 1) 优先严格全等
            var j = FindUnusedActual(expected[i], actual, usedActual, allowExDiff, strict: true);
            if (j >= 0)
            {
                usedActual[j] = true;
                continue;
            }

            // 2) 起点身份相同但 segments/duration/终点因平行重连错配：暂缓，segment 进池
            j = FindUnusedActual(expected[i], actual, usedActual, allowExDiff, strict: false);
            if (j >= 0)
            {
                usedActual[j] = true;
                DiffSegmentsToPools(expected[i], actual[j], expectedSegPool, actualSegPool);
                deferredFail ??=
                    $"Note mismatch at index {i}:{Environment.NewLine}" +
                    $"EXPECTED: {FormatNote(expected[i])}{Environment.NewLine}" +
                    $"ACTUAL  : {FormatNote(actual[j])}";
                continue;
            }

            // 3) 无对应 actual（例如同起点被 C2S 合并进另一条）：整 note 的 segment 进期望池
            if (expected[i].Segments.Count > 0)
            {
                expectedSegPool.AddRange(ExpandSegments(expected[i]));
                deferredFail ??=
                    $"Note mismatch at index {i}:{Environment.NewLine}" +
                    $"EXPECTED: {FormatNote(expected[i])}{Environment.NewLine}" +
                    $"ACTUAL  : {EOF} (absorbed into segment pool)";
                continue;
            }

            Assert.Fail(
                $"Note mismatch at index {i}:{Environment.NewLine}" +
                $"EXPECTED: {FormatNote(expected[i])}{Environment.NewLine}" +
                $"ACTUAL  : {EOF}");
        }

        for (var j = 0; j < actual.Count; j++)
        {
            if (usedActual[j]) continue;

            // 多余的 actual（合并/错接多出来的尾巴挂在别的起点上时也可能出现）：segment 进实际池
            if (actual[j].Segments.Count > 0)
            {
                actualSegPool.AddRange(ExpandSegments(actual[j]));
                deferredFail ??=
                    $"Note mismatch (unmatched actual at {j}):{Environment.NewLine}" +
                    $"EXPECTED: {EOF}{Environment.NewLine}" +
                    $"ACTUAL  : {FormatNote(actual[j])} (absorbed into segment pool)";
                continue;
            }

            Assert.Fail(
                $"Note mismatch (unmatched actual at {j}):{Environment.NewLine}" +
                $"EXPECTED: {EOF}{Environment.NewLine}" +
                $"ACTUAL  : {FormatNote(actual[j])}");
        }

        if (deferredFail is null)
            return;

        if (MatchSegmentPools(expectedSegPool, actualSegPool))
            return;

        Assert.Fail(
            deferredFail + Environment.NewLine +
            $"Segment pool not cleared after cross-note matching " +
            $"(expected leftover={expectedSegPool.Count}, actual leftover={actualSegPool.Count})." + Environment.NewLine +
            FormatSegPool("EXPECTED leftover", expectedSegPool) + Environment.NewLine +
            FormatSegPool("ACTUAL leftover", actualSegPool));
    }

    /// <summary>
    /// 在尚未占用的 actual 中查找与 expected 匹配的音符。
    /// strict：整 note 等价；否则仅起点身份等价（忽略 duration/终点）。
    /// </summary>
    private static int FindUnusedActual(
        ChuNote expected,
        List<ChuNote> actual,
        bool[] usedActual,
        bool allowExDiff,
        bool strict)
    {
        for (var j = 0; j < actual.Count; j++)
        {
            if (usedActual[j]) continue;
            if (strict)
            {
                if (CompareNote(expected, actual[j], allowExDiff)) return j;
            }
            else if (CompareNoteHead(expected, actual[j], allowExDiff, requireDuration: false))
            {
                return j;
            }
        }
        return -1;
    }

    /// <summary>
    /// 比较两个音符是否实质等同；时间与时长等字段可命中宽容规则（见测试类内常量与分支注释）。
    /// </summary>
    public static bool CompareNote(ChuNote expected, ChuNote actual, bool allowExDiff = false)
    {
        if (!CompareNoteHead(expected, actual, allowExDiff)) return false;
        if (expected.EndCell != actual.EndCell || expected.EndWidth != actual.EndWidth) return false;
        if (Math.Abs(EndHeightOf(expected) - EndHeightOf(actual)) > 0.05m) return false;
        if (!SegmentsEquivalent(expected, actual)) return false;
        return true;
    }

    /// <summary>
    /// Note 头部等价（不含终点位置与 Segments）。
    /// <paramref name="requireDuration"/> 为 false 时连 duration 也不比——平行重连错配后总长常被改写。
    /// </summary>
    private static bool CompareNoteHead(ChuNote expected, ChuNote actual, bool allowExDiff = false, bool requireDuration = true)
    {
        if (!TypesEquivalent(expected, actual, allowExDiff)) return false;
        if (!TimesEquivalent(expected.Time, actual.Time)) return false;
        if (requireDuration && !DurationsEquivalent(expected, actual)) return false;
        if (expected.Cell != actual.Cell || expected.Width != actual.Width) return false;
        if (Math.Abs(expected.Height - actual.Height) > 0.05m) return false;
        if (!CrushIntervalsEquivalent(expected.CrushInterval, actual.CrushInterval)) return false;
        if (!AttrsEquivalent(expected, actual, allowExDiff)) return false;
        if (!TargetNotesEquivalent(expected, actual, allowExDiff)) return false;
        return true;
    }

    private static decimal EndHeightOf(ChuNote n) =>
        n.Segments.Count > 0 ? n.Segments[^1].EndHeight : n.Height;

    /// <summary>规则 (a)：time 相差 ≤ 1/768 视为相等。</summary>
    private static bool TimesEquivalent(Rational a, Rational b) => (a - b).Abs() <= Tol768;

    private static bool CrushIntervalsEquivalent(Rational? e, Rational? a)
    {
        if (e is null && a is null) return true;
        if (e is null || a is null) return false;
        return TimesEquivalent(e.Value, a.Value);
    }

    /// <summary>
    /// 类型比较。<paramref name="allowExDiff"/> 为 true 时，Hold/Slide 的 Ex 标志位差异可忽略
    /// （对应旧 HLD/HXD、SLD/SXD、SLC/SXC、AHD/AHX）；否则要求严格相等。
    /// Air Tap 还需 AirDirection 一致。CHR/TAP 即使 allowExDiff 也不互通。
    /// </summary>
    private static bool TypesEquivalent(ChuNote e, ChuNote a, bool allowExDiff)
    {
        if (e.Type != a.Type || e.IsAir != a.IsAir) return false;
        if (IsAir(e) && e.AirDirection != a.AirDirection) return false;
        if (e.IsEx == a.IsEx) return true;
        if (!allowExDiff) return false;
        // 仅 Hold/Slide（含 Air Hold）允许 Ex 差异；Tap 的 CHR vs TAP 不允许
        return e.Type is ChuNoteType.Hold or ChuNoteType.Slide;
    }

    /// <summary>
    /// 规则 (b)：|Δduration| ≤ 1/768，或（|Δduration| ≤ 1/384 且 |ΔendTime| ≤ 1/768）时视为 duration 语义相等。
    /// </summary>
    private static bool DurationsEquivalent(ChuNote e, ChuNote a)
    {
        var dd = (e.Duration - a.Duration).Abs().CanonicalForm;
        return dd <= Tol768 || (dd <= Tol384 && (e.EndTime - a.EndTime).Abs() <= Tol768);
    }

    /// <summary>
    /// 规则 (c)(d)：广义 Air 的 Color；Flick 方向；allowExDiff 时非 Ex 音符可无 Ex。
    /// Crush（ALD）不比较 Color（C2S 侧观测不支持颜色 tag）。
    /// </summary>
    private static bool AttrsEquivalent(ChuNote e, ChuNote a, bool allowExDiff = false)
    {
        if (e.Type == ChuNoteType.Crush) return true;

        if (e.IsAir && e.Color != a.Color)
        {
            // 旧 Tag 的 DEF/空串互通；新模型默认均为 DEF，一般不会走到这里
            if (!((e.Color == NoteColor.DEF || a.Color == NoteColor.DEF) && e.Color != a.Color))
                return false;
            // 若一侧为 DEF、另一侧非 DEF，则不相等（上面已排除双 DEF）
            return false;
        }

        if (e.Ex == a.Ex) return true;

        if (allowExDiff && TypesEquivalent(e, a, allowExDiff: true) && e.IsEx != a.IsEx)
        {
            // 有 Ex 的一侧 vs 无 Ex 的一侧：允许
            return true;
        }

        // Flick：C2S 的方向字段恒为 L，不表示真实方向，语义等同 UGC Auto（A / Ex=null）。
        // 因此经 C2S 往返后 Ex 会变成 null；与 UGC 侧的 L/R/A（LS/RS/null）均视为等价。
        if (e.Type == ChuNoteType.Flick && a.Type == ChuNoteType.Flick &&
            (e.Ex is null || a.Ex is null))
            return true;

        return false;
    }

    /// <summary>
    /// TargetNote：Crush 跳过；其余比较 AsC2sPreviousStr。
    /// 非 Air 的 Slide 通常无 TargetNote（段已合并进 Segments）。
    /// </summary>
    private static bool TargetNotesEquivalent(ChuNote e, ChuNote a, bool allowExDiff)
    {
        if (e.Type == ChuNoteType.Crush || a.Type == ChuNoteType.Crush) return true;

        var et = AsC2sPreviousStr(e.TargetNote) ?? "N";
        var at = AsC2sPreviousStr(a.TargetNote) ?? "N";
        if (et == at) return true;

        if (e.TargetNote != null && a.TargetNote != null &&
            TypesEquivalent(e.TargetNote, a.TargetNote, allowExDiff))
            return true;

        // 普通 Slide：TargetNote 可有可无（新旧 C2S / 段合并后的差异）
        if (e.Type == ChuNoteType.Slide && a.Type == ChuNoteType.Slide && !e.IsAir && !a.IsAir)
        {
            var etN = et is "" or "N" ? "SLD" : et;
            var atN = at is "" or "N" ? "SLD" : at;
            return etN == atN;
        }

        return false;
    }

    private static bool SegmentsEquivalent(ChuNote e, ChuNote a)
    {
        if (e.Segments.Count != a.Segments.Count) return false;
        for (var i = 0; i < e.Segments.Count; i++)
        {
            var es = e.Segments[i];
            var @as = a.Segments[i];
            // C 标志仅对 Slide（SLC/ASC）在 C2S 中有对应；Hold/Crush 的 s/c 在进 C2S 后会丢失
            if (e.Type == ChuNoteType.Slide && es.C != @as.C) return false;
            if (!TimesEquivalent(es.Length, @as.Length))
            {
                var dd = (es.Length - @as.Length).Abs().CanonicalForm;
                if (dd > Tol384) return false;
            }
            if (es.EndCell != @as.EndCell || es.EndWidth != @as.EndWidth) return false;
            if (Math.Abs(es.EndHeight - @as.EndHeight) > 0.05m) return false;
        }
        return true;
    }

    private static List<AbsSeg> ExpandSegments(ChuNote n)
    {
        var list = new List<AbsSeg>(n.Segments.Count);
        var t = n.Time;
        var cell = n.Cell;
        var width = n.Width;
        var height = n.Height;
        foreach (var s in n.Segments)
        {
            list.Add(new AbsSeg(
                n.Type, n.IsAir, s.C,
                t, cell, width, height,
                s.Length, s.EndCell, s.EndWidth, s.EndHeight));
            t = (t + s.Length).CanonicalForm;
            cell = s.EndCell;
            width = s.EndWidth;
            height = s.EndHeight;
        }
        return list;
    }

    private static bool AbsSegsEquivalent(AbsSeg e, AbsSeg a)
    {
        if (e.Type != a.Type || e.IsAir != a.IsAir) return false;
        if (e.Type == ChuNoteType.Slide && e.C != a.C) return false;
        if (!TimesEquivalent(e.StartTime, a.StartTime)) return false;
        if (e.StartCell != a.StartCell || e.StartWidth != a.StartWidth) return false;
        if (Math.Abs(e.StartHeight - a.StartHeight) > 0.05m) return false;
        if (!TimesEquivalent(e.Length, a.Length))
        {
            var dd = (e.Length - a.Length).Abs().CanonicalForm;
            if (dd > Tol384) return false;
        }
        if (e.EndCell != a.EndCell || e.EndWidth != a.EndWidth) return false;
        if (Math.Abs(e.EndHeight - a.EndHeight) > 0.05m) return false;
        return true;
    }

    /// <summary>
    /// 将两个 note 的 segments 做多重集差分：能就地配对的消掉，剩余分别进入期望/实际池。
    /// </summary>
    private static void DiffSegmentsToPools(ChuNote e, ChuNote a, List<AbsSeg> expectedPool, List<AbsSeg> actualPool)
    {
        var eSegs = ExpandSegments(e);
        var aSegs = ExpandSegments(a);
        var usedA = new bool[aSegs.Count];
        foreach (var es in eSegs)
        {
            var found = -1;
            for (var j = 0; j < aSegs.Count; j++)
            {
                if (usedA[j]) continue;
                if (!AbsSegsEquivalent(es, aSegs[j])) continue;
                found = j;
                break;
            }
            if (found >= 0) usedA[found] = true;
            else expectedPool.Add(es);
        }
        for (var j = 0; j < aSegs.Count; j++)
        {
            if (!usedA[j]) actualPool.Add(aSegs[j]);
        }
    }

    /// <summary>
    /// 尝试消解跨 note 错配后的 segment 池；成功则两侧皆空。
    /// </summary>
    private static bool MatchSegmentPools(List<AbsSeg> expectedPool, List<AbsSeg> actualPool)
    {
        var usedA = new bool[actualPool.Count];
        var leftoverE = new List<AbsSeg>();
        foreach (var es in expectedPool)
        {
            var found = -1;
            for (var j = 0; j < actualPool.Count; j++)
            {
                if (usedA[j]) continue;
                if (!AbsSegsEquivalent(es, actualPool[j])) continue;
                found = j;
                break;
            }
            if (found >= 0) usedA[found] = true;
            else leftoverE.Add(es);
        }

        var leftoverA = new List<AbsSeg>();
        for (var j = 0; j < actualPool.Count; j++)
        {
            if (!usedA[j]) leftoverA.Add(actualPool[j]);
        }

        expectedPool.Clear();
        expectedPool.AddRange(leftoverE);
        actualPool.Clear();
        actualPool.AddRange(leftoverA);
        return expectedPool.Count == 0 && actualPool.Count == 0;
    }

    private static string FormatSegPool(string label, IReadOnlyList<AbsSeg> pool)
    {
        if (pool.Count == 0) return $"{label}: (empty)";
        var lines = pool.Select(s =>
            $"  {(s.C ? "C" : "S")}:{s.Length} @{s.StartTime} ({s.StartCell},{s.StartWidth})->({s.EndCell},{s.EndWidth}) " +
            $"type={s.Type}/air={s.IsAir} h={s.StartHeight}->{s.EndHeight}");
        return $"{label} ({pool.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    private static string FormatNote(ChuNote n)
    {
        var type = n switch
        {
            { Type: ChuNoteType.Tap, IsAir: true } => n.AirDirection.ToString(),
            { Type: ChuNoteType.Tap, IsEx: true } => "CHR",
            { Type: ChuNoteType.Tap } => "TAP",
            { Type: ChuNoteType.Flick } => "FLK",
            { Type: ChuNoteType.Mine } => "MNE",
            { Type: ChuNoteType.Hold, IsAir: true, IsEx: true } => "AHX",
            { Type: ChuNoteType.Hold, IsAir: true } => "AHD",
            { Type: ChuNoteType.Hold, IsEx: true } => "HXD",
            { Type: ChuNoteType.Hold } => "HLD",
            { Type: ChuNoteType.Slide, IsAir: true } => "ASD",
            { Type: ChuNoteType.Slide, IsEx: true } => "SXD",
            { Type: ChuNoteType.Slide } => "SLD",
            { Type: ChuNoteType.Crush } => "ALD",
            _ => n.Type.ToString(),
        };
        var tgt = AsC2sPreviousStr(n.TargetNote) ?? "N";
        var segs = string.Join('+', n.Segments.Select(s =>
            $"{(s.C ? "C" : "S")}:{s.Length}->({s.EndCell},{s.EndWidth},{s.EndHeight})"));
        return $"{type} t={n.Time} start=({n.Cell},{n.Width}) dur={n.Duration} end=({n.EndCell},{n.EndWidth}) " +
               $"ex={n.Ex} color={n.Color} tgt={tgt} h=({n.Height},{EndHeightOf(n)}) crush={n.CrushInterval} segs=[{segs}]";
    }

    /// <summary>
    /// 比较两份 C2S 文本：忽略头部元信息（TUTORIAL 及之前）与 SLA（分音符变速区时长策略与 PenguinTools 不同），
    /// 各行按规范化排序键排序后逐行匹配（允许原始行序不同）。
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

    /// <summary>除 ALD interval 的 `$` 宽松规则、ALD durationTicks ±1、ALD Height/EndHeight ±0.1、Hold/Slide 可选 SLD 后缀与 Ex 方向外，要求整行一致。</summary>
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

    private static readonly HashSet<string> C2sDirectionTags = Enum.GetNames<ExDirection>().ToHashSet();

    /// <summary>
    /// HLD/HXD/SLC/SLD/SXC/SXD：可选 TargetNote 后缀（SLD）；末尾方向标识符（ExDirection）任一侧可省略，两侧都有时必须一致。
    /// </summary>
    private static bool HoldSlideC2sLinesEquivalent(string expected, string actual)
    {
        var e = expected.Split('\t');
        var a = actual.Split('\t');
        if (e.Length == 0 || a.Length == 0 || e[0] != a[0]) return false;
        if (e[0] is not ("HLD" or "HXD" or "SLC" or "SLD" or "SXC" or "SXD")) return false;

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
        f[0] is ("SLC" or "SLD" or "SXC" or "SXD") && f.Length > 8 && f[^1] == "SLD" ? f[..^1] : f;

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
            .Where(line => !IsC2sHeaderLine(line) && !line.StartsWith("SLA\t", StringComparison.Ordinal))
            .OrderBy(C2sCompareSortKey, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 排序键：ALD 忽略 interval/durationTicks；Hold/Slide 忽略可选 SLD 后缀与 Ex 方向，避免与 PenguinTools 的后缀差异打乱对齐。
    /// </summary>
    private static string C2sCompareSortKey(string line)
    {
        if (line.StartsWith("ALD\t", StringComparison.Ordinal))
            return AldAwareC2sSortKey(line);

        var fields = line.Split('\t');
        if (fields.Length > 0 && fields[0] is "HLD" or "HXD" or "SLC" or "SLD" or "SXC" or "SXD")
        {
            fields = StripOptionalDirectionTag(fields);
            fields = StripOptionalSlideTargetNote(fields);
            fields = StripOptionalDirectionTag(fields);
            return string.Join('\t', fields);
        }

        return line;
    }

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
        // CLICK（UGC `c`）在 Parser 中已忽略，不会进入 Notes
        var (c2sReparsed, _) = new C2sParser().Parse(c2sText);
        Assert.NotEmpty(c2sReparsed.Notes);
        AssertNotesEqual(ugc.Notes, c2sReparsed.Notes);

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

        // Sheriruth Expert/Master 存在同位置 CHR+HLD/SLD 叠放（非 ExLong）；默认会把 CHR 消费进长条，
        // 导致与官谱 IR 不对齐，故仅对这两张开启 NoExLong。
        var noExLong = c2sPath.Contains("2351_02") || c2sPath.Contains("2351_03");

        // 再把转出来的ugc，parse回去，比较是否和一开始的c2s等价
        var (ugcReparsed, _) = new UgcParser(noExLong: noExLong).Parse(ugcText);
        Assert.NotEmpty(ugcReparsed.Notes);
        AssertNotesEqual(c2s.Notes, ugcReparsed.Notes, allowExDiff: true);

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
