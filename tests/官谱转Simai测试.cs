using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MuConvert.chart;
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
        SimaiCommaTimeline.AssertTimelineEqual(expectedTimeline, actualTimeline, chart, _output);
    }
}

/// <summary>
/// 按 Simai 文法 <c>chart: (notations ',')*</c> 将谱面切成顶层逗号分段，并在每个分段上复现与
/// <see cref="MuConvert.parser.simai.SimaiParser"/> 一致的 <c>now</c> / <c>step</c> 推进规则，
/// 得到 (时刻, 原文) 序列；不构造 Note，不把片段再交给 SimaiParser。
/// </summary>
internal static partial class SimaiCommaTimeline
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

    public static void AssertTimelineEqual(
        IReadOnlyList<Entry> expected,
        IReadOnlyList<Entry> actual,
        Chart chart,
        ITestOutputHelper? output = null)
    {
        static IEnumerable<Entry> Canon(IReadOnlyList<Entry> e) =>
            e.Select(x => new Entry(x.Time, NormalizeForCompare(x.Text)))
                .OrderBy(p => p.Time)
                .ThenBy(p => p.Text, StringComparer.Ordinal);

        expected = Canon(expected).ToList();
        actual = Canon(actual).ToList();
        Assert.Equal(expected.Count, actual.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            try
            {
                Assert.Equal(expected[i].Time, actual[i].Time);
                AssertNoteEqual(expected[i].Text, actual[i].Text, i, actual[i].Time, chart);
            }
            catch (Xunit.Sdk.XunitException e)
            {
                output?.WriteLine(FormatNeighborhood(expected, actual, i).TrimEnd());
                throw;
            }
        }
    }

    [GeneratedRegex(@"\[(?:(\d+):(\d+)|(?:([\d\.]+)#)?#([\d\.]+))\]")]
    private static partial Regex DurationStrRegex();
    
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-3;
    
    private static void AssertNoteEqual(string expected, string actual, int noteIdx, Rational time, Chart chart)
    {
        var expArr = RearrangeNote(expected).Split('/', '`');
        var actArr = RearrangeNote(actual).Split('/', '`');
        var max = Math.Max(expArr.Length, actArr.Length);

        for (var i = 0; i < max; i++)
        {
            var exp = i < expArr.Length ? expArr[i] : "<EOF>";
            var act = i < actArr.Length ? actArr[i] : "<EOF>";
            var result = exp == act;
            
            if (!result)
            {
                // 尝试是否是只有时间不匹配，如果是的话，允许一定的阈值
                var expTime = DurationStrRegex().Match(exp);
                var actTime = DurationStrRegex().Match(act);
                var bpm = chart.BpmList.Find(time).Bpm;
                if (expTime.Groups[1].Success && actTime.Groups[4].Success)
                { // exp中是分数时间、act中是小数时间的情况
                    // 小数时间化为分数时间，看看是否对的上
                    var numer = decimal.Parse(actTime.Groups[4].Value) / (240 / bpm) * int.Parse(expTime.Groups[1].Value);
                    if (Math.Round(numer) == int.Parse(expTime.Groups[2].Value)) result = true; // 如果对的上，则不判定为比较失败
                    if (actTime.Groups[3].Success)
                    { // 如果写了等待时间的话，等待时间必须为1拍
                        var oneBeatSec = 60 / (Rational)bpm;
                        if (!Near(double.Parse(actTime.Groups[3].Value), (double)oneBeatSec)) result = false; // 如果等待时间对不上，则仍判定为比较失败
                    }
                }
                else if (actTime.Groups[1].Success && expTime.Groups[4].Success)
                { // exp中是小数时间、act中是分数时间的情况
                    // 分数时间化为小数时间，看是否对的上（差距<1ms）
                    var sec = new Rational(int.Parse(actTime.Groups[2].Value), int.Parse(actTime.Groups[1].Value)) * (240 / (Rational)bpm);
                    if (Near((double)sec, double.Parse(expTime.Groups[4].Value))) result = true; // 如果对的上，则不判定为比较失败
                    if (expTime.Groups[3].Success)
                    { // 如果写了等待时间的话，等待时间必须为1拍
                        var oneBeatSec = 60 / (Rational)bpm;
                        if (!Near(double.Parse(expTime.Groups[3].Value), (double)oneBeatSec)) result = false; // 如果等待时间对不上，则仍判定为比较失败
                    }
                }
            }

            if (!result) Assert.Fail(
                $"First difference at Notation {noteIdx + 1} (time {time}):{Environment.NewLine}" +
                $"EXPECTED: {expected}{Environment.NewLine}" +
                $"ACTUAL  : {actual}"
            );
        }
    }

    private static string RearrangeNote(string s)
    {
        return string.Join('`', s.Split('`').Select(x =>
        {
            var s = x.Split('/');
            s.Sort();
            return string.Join('/', s);
        }));
    }

    private static string FormatNeighborhood(IReadOnlyList<Entry> a, IReadOnlyList<Entry> b, int i)
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- context (expected) ---");
        for (var j = Math.Max(0, i - 3); j < Math.Min(a.Count, i + 5); j++)
            sb.AppendLine($"  [{j}] {a[j].Time} | {a[j].Text}");
        sb.AppendLine("--- context (actual) ---");
        for (var j = Math.Max(0, i - 3); j < Math.Min(b.Count, i + 5); j++)
            sb.AppendLine($"  [{j}] {b[j].Time} | {b[j].Text}");
        return sb.ToString();
    }

    /// <summary>
    /// 去掉空白、统一 b/x/f 连续修饰符的字典序，便于与「别的转谱器」对照。
    /// </summary>
    internal static string NormalizeForCompare(string s)
    {
        s = s.Trim().Replace("\r", "").Replace("\n", "");
        s = Regex.Replace(s, @"\s+", "");
        s = Regex.Replace(s, @"(\[[\d\.#:]+\])b", m => "b" + m.Groups[1].Value); // 其实严格根据文档，对星星"1-4[8:3]b"是正确的，而对hold"4hb[8:3]"才是正确的。我们的SimaiGenerator是严格按标准输出的，但出于比较的简单考虑，还是全部统一到"4hb[8:3]"这种情况下，处理起来简单一点。
        s = Regex.Replace(s, "[bxfh]{2,}", m => new string(m.Value.OrderBy(c => c).ToArray()));
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
