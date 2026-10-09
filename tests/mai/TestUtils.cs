using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MuConvert.mai;
using MuConvert.utils;
using Rationals;
using Xunit.Abstractions;
using YamlDotNet.Serialization;

namespace MuConvert.Tests.mai;

internal static class TestUtils
{
    private static readonly Regex Ma2ClkDefLineRegex = new(@"^CLK_DEF\t(\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Ma2ClkLineRegex = new(@"^CLK\t(\d+)\s", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // 查找到测试数据的根目录(tests/mai/testset)
    public static DirectoryInfo FindTestsetRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "MuConvert.Tests.csproj")))
            dir = Path.GetDirectoryName(dir);
        return new DirectoryInfo(Path.Combine(dir ?? throw new DirectoryNotFoundException("Could not locate repo root."), "mai", "testset"));
    }
    
    /// <summary>
    /// 自 MA2 文本中用正则匹配首行 <c>CLK_DEF\t…</c>（官机头字段名；部分资料误写为 CLOCK_DEF），返回其整数值；
    /// 与 <see cref="MaiChart.ClockCount"/> 的关系为 <c>CLK_DEF = 96 * ClockCount</c>（<c>RESOLUTION</c> 为 384 时）。
    /// </summary>
    public static int? TryParseMa2ClkDef(string ma2Text)
    {
        var m = Ma2ClkDefLineRegex.Match(ma2Text);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var v)) return null;
        if (v == 0)
        { // 对CLK_DEF为0的情况，就数一下CLK指令的个数，等效一下
            var rawClkLines = Ma2ClkLineRegex.Matches(ma2Text);
            v = rawClkLines.Count * 96;
        }
        return v;
    }

    /// <summary>解析 <c>VERSION</c> 行第三列（如 <c>1.03.00</c>）为整数版本号（如 103）；未找到则返回 <c>null</c>。</summary>
    public static int? TryParseMa2HeaderVersion(string ma2Text)
    {
        foreach (var raw in ma2Text.EnumerateLines())
        {
            if (raw.IsWhiteSpace()) continue;
            var line = raw.ToString().TrimEnd('\r');
            var parts = line.Split('\t');
            if (parts.Length < 3 || parts[0] != "VERSION")
                continue;
            var ver = parts[2].Split('.');
            if (ver.Length >= 2 &&
                int.TryParse(ver[0], out var major) &&
                int.TryParse(ver[1], out var minor))
                return major * 100 + minor;
            return null;
        }
        return null;
    }

    public static (int, int) ExtractMa2Time(string ma2Line)
    {
        var para = ma2Line.Split('\t');
        return (int.Parse(para[1]), int.Parse(para[2]));
    }

    public static bool IsSameTime(string ma2Line1, string ma2Line2) => ExtractMa2Time(ma2Line1) == ExtractMa2Time(ma2Line2);

    /// <summary>
    /// 提取 MA2 音符段至 <c>T_REC</c> 之前：跳过头部与 <c>BPM</c> 行；若存在 <c>MET\t</c> 小节行则跳过该行；
    /// 部分旧官谱 golden 无 <c>MET</c>，则在 <c>BPM</c> 块后的首条非头行开始收集。
    /// </summary>
    public static string KeepNotesOnly(string text)
    {
        var result = new StringBuilder();
        var inNotes = false;
        foreach (var l in text.EnumerateLines())
        {
            var line = l.ToString().TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("T_REC", StringComparison.Ordinal)) break;

            if (!inNotes)
            {
                if (IsMa2HeaderOrBpmLine(line)) continue;
                inNotes = true;
            }

            result.Append(line).Append('\n');
        }

        return result.ToString().Trim();
    }

    private static bool IsMa2HeaderOrBpmLine(string line) =>
        line.StartsWith("VERSION\t", StringComparison.Ordinal) ||
        line.StartsWith("FES_MODE\t", StringComparison.Ordinal) ||
        line.StartsWith("BPM_DEF\t", StringComparison.Ordinal) ||
        line.StartsWith("MET_DEF\t", StringComparison.Ordinal) ||
        line.StartsWith("RESOLUTION\t", StringComparison.Ordinal) ||
        line.StartsWith("CLK_DEF\t", StringComparison.Ordinal) ||
        line.StartsWith("COMPATIBLE_CODE\t", StringComparison.Ordinal) ||
        line.StartsWith("GENERATED_BY\t", StringComparison.Ordinal) ||
        line.StartsWith("BPM\t", StringComparison.Ordinal) ||
        line.StartsWith("MET\t", StringComparison.Ordinal) ||
        line.StartsWith("CLK\t", StringComparison.Ordinal);

    /// <summary>
    /// 比较 MA2 音符行：逐行对齐；同 tick 内允许顺序不同；slide 长度允许 ±1（CN 另允许尾时刻对齐）。
    /// </summary>
    public static void AssertMa2NotesEqual(string expected, string actual, string? context = null)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var max = Math.Max(expectedLines.Length, actualLines.Length);
        var prefix = string.IsNullOrEmpty(context) ? "" : $"{context}: ";

        for (var i = 0; i < max; i++)
        {
            var exp = i < expectedLines.Length ? expectedLines[i] : "<EOF>";
            var act = i < actualLines.Length ? actualLines[i] : "<EOF>";
            var result = CompareMa2NoteLine(exp, act);
            if (!result && i < actualLines.Length)
            {
                // 尝试同一时刻的其他 expected 行：匹配则交换到当前位置
                var j = i + 1;
                while (j < expectedLines.Length)
                {
                    if (CompareMa2NoteLine(expectedLines[j], act))
                    {
                        (expectedLines[j], expectedLines[i]) = (expectedLines[i], expectedLines[j]);
                        result = true;
                        break;
                    }

                    if (IsSameTime(expectedLines[j], act))
                    {
                        j++;
                        continue;
                    }

                    break;
                }
            }

            if (!result)
            {
                Assert.Fail(
                    $"{prefix}first difference at line {i + 1}:{Environment.NewLine}" +
                    $"EXPECTED: {exp}{Environment.NewLine}" +
                    $"ACTUAL  : {act}");
            }
        }
    }

    private static (int TimeTick, int Len, string Extra) GetSlideTime(string slide)
    {
        var values = slide.Split('\t');
        return (int.Parse(values[1], CultureInfo.InvariantCulture) * 384 + int.Parse(values[2], CultureInfo.InvariantCulture),
            int.Parse(values[5], CultureInfo.InvariantCulture),
            string.Join("\t", values[0], values[3], values[4], values[6]));
    }

    private static bool CompareMa2NoteLine(string exp, string act)
    {
        var result = string.Equals(exp, act, StringComparison.Ordinal);
        if (!result && exp.Length >= 5 && act.Length >= 5 && exp[..5] == act[..5] && SlideTypeTool.IsSlide(exp[2..5]))
        {
            var (expTime, expLen, expExtra) = GetSlideTime(exp);
            var (actTime, actLen, actExtra) = GetSlideTime(act);
            if (expExtra != actExtra) return result;
            if (exp[..2] == "CN")
            {
                // CN：要么尾时刻完全对，要么长度至多差 1
                if (expTime + expLen == actTime + actLen || Math.Abs(expLen - actLen) <= 1) result = true;
            }
            else
            {
                // 首段：开始时刻必须对且长度至多差 1
                if (expTime == actTime && Math.Abs(expLen - actLen) <= 1) result = true;
            }
        }

        return result;
    }
    
    public static MaiChart LoadOneChart(out List<Alert> alerts)
    {
        var maidataPath = Path.Combine(FindTestsetRoot().FullName, "官谱", "Xaleid◆scopiX [DX]", "maidata.txt");
        Assert.True(File.Exists(maidataPath), $"Missing test maidata: {maidataPath}");

        var maidata = new Maidata(File.ReadAllText(maidataPath, Encoding.UTF8));
        Assert.True(maidata.Levels.ContainsKey(6), "Expected lv6 (inote_6) in maidata.");
        var chartInfo = maidata.Levels[6];

        var (chart, parseAlerts) = new SimaiParser(clockCount: maidata.ClockCount)
            .Parse(chartInfo.Inote);
        alerts = parseAlerts;
        chart.Sort();
        
        Assert.NotEmpty(chart.Notes);
        Assert.NotEmpty(chart.BpmList);
        Assert.True(chart.BpmList[0].Time == 0, "sanity");
        Assert.DoesNotContain(alerts, a => a.Level >= Alert.LEVEL.Error);
        return chart;
    }
    
    public static IEnumerable<object[]> GetTestInputs(string dataDir, int? lv = null, string? title = null)
    {
        var testsetRoot = Path.Combine(FindTestsetRoot().FullName, dataDir);
        if (!Directory.Exists(testsetRoot))
            throw new DirectoryNotFoundException($"Testset root not found: {testsetRoot}");

        foreach (var maidataPath in Directory.EnumerateFiles(testsetRoot, "maidata.txt", SearchOption.AllDirectories))
        {
            var maidataTxt = File.ReadAllText(maidataPath, Encoding.UTF8);
            var maidata = new Maidata(maidataTxt);
            foreach (var id in maidata.Levels.Keys.OrderBy(k => k))
            {
                // 如果指定了lv或title、但与要求不符，则不返回
                if ((lv != null && id != lv) || (title != null && !maidataPath.Contains("title"))) continue;
                yield return [new TestInput(maidataPath, id)];
            }
        }
    }

    public static void AssertSimaiNotesEqual(string expected, string actual, MaiChart chart,
        ITestOutputHelper? outputHelper = null)
    {
        var expectedTimeline = SimaiCommaTimeline.Flatten(expected);
        var actualTimeline = SimaiCommaTimeline.Flatten(actual);
        SimaiCommaTimeline.AssertTimelineEqual(expectedTimeline, actualTimeline, chart, outputHelper);
    }
}

public sealed class TestSegment
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>YAML 文件的原始文件名（不含目录）。</summary>
    [YamlIgnore]
    public string YamlFileName { get; private set; } = "";
    [YamlMember(Alias = "simai")]
    public string Simai { get; set; } = "";
    [YamlMember(Alias = "ma2")]
    public string Ma2 { get; set; } = "";

    public override string ToString() => YamlFileName;

    public static TestSegment Load(string yamlPath)
    {
        var yamlFileName = Path.GetFileName(yamlPath);
        var text = File.ReadAllText(yamlPath, Encoding.UTF8);
        var seg = YamlDeserializer.Deserialize<TestSegment>(text)
                  ?? throw new FormatException($"{yamlPath}: 空 YAML 或根节点无法解析为映射");

        seg.YamlFileName = yamlFileName;
        seg.Simai = seg.Simai.Trim();
        seg.Ma2 = seg.Ma2.Trim();
        return seg;
    }

    public bool HasSimai => !string.IsNullOrWhiteSpace(Simai);
    public bool HasMa2 => !string.IsNullOrWhiteSpace(Ma2);
}

public record TestInput(string Maidata, int LevelId)
{
    public string Dir = Path.GetDirectoryName(Maidata)!;

    public string MA2
    {
        get
        {
            var expectedSuffix = $"{LevelId - 2:D2}.ma2";
            var dirInfo = new DirectoryInfo(Dir);
            var expected = dirInfo.EnumerateFiles("*" + expectedSuffix, SearchOption.TopDirectoryOnly).ToList();
            Assert.True(expected.Count == 1,
                $"Expected exactly one golden file matching '*{expectedSuffix}' in '{dirInfo.FullName}', got {expected.Count}.");
            return expected[0].FullName;
        }
    }
    
    public override string ToString() => $"{Path.GetFileName(Dir)}-lv{LevelId}";
}

/// <summary>
/// 按 Simai 文法 <c>chart: (notations ',')*</c> 将谱面切成顶层逗号分段，并在每个分段上复现与
/// <see cref="MuConvert.mai.SimaiParser"/> 一致的 <c>now</c> / <c>step</c> 推进规则，
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
        MaiChart chart,
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
            catch (Xunit.Sdk.XunitException)
            {
                output?.WriteLine(FormatNeighborhood(expected, actual, i).TrimEnd());
                throw;
            }
        }
    }

    [GeneratedRegex(@"\[(?:([\d\.]+)##)?(?:(\d+):(\d+)|#?([\d\.]+))\]")]
    private static partial Regex DurationStrRegex();
    
    /// <summary>时长比较容差：当前 BPM 下 1/384 小节对应的秒数（与 MA2 RESOLUTION 对齐）。</summary>
    private static bool Near(double a, double b, decimal bpm) =>
        Math.Abs(a - b) < (double)(240m / bpm / 384);
    
    private static void AssertNoteEqual(string expected, string actual, int noteIdx, Rational time, MaiChart chart)
    {
        var expArr = RearrangeNote(expected).Split('/', '`', '*');
        var actArr = RearrangeNote(actual).Split('/', '`', '*');
        var max = Math.Max(expArr.Length, actArr.Length);

        for (var i = 0; i < max; i++)
        {
            var exp = i < expArr.Length ? expArr[i] : "<EOF>";
            var act = i < actArr.Length ? actArr[i] : "<EOF>";
            var result = exp == act;
            
            if (!result && exp.StartsWith("C1"))
            { // groundtruth里有一部分是写成了C1，此时不要报错，应该给予兼容。
                exp = exp.Replace("C1", "C");
                result = exp == act;
            }
            
            if (!result) result = CompareDurationStr(exp, act, time, chart);

            if (!result) Assert.Fail(
                $"First difference at Notation {noteIdx + 1} (time {time}):{Environment.NewLine}" +
                $"EXPECTED: {expected}{Environment.NewLine}" +
                $"ACTUAL  : {actual}"
            );
        }
    }

    private static bool CompareDurationStr(string exp, string act, Rational time, MaiChart chart)
    {
        bool result = false;
        // 尝试是否是只有时间不匹配，如果是的话，允许一定的阈值
        var expTime = DurationStrRegex().Match(exp);
        var actTime = DurationStrRegex().Match(act);
        if (!expTime.Success || !actTime.Success) return result;
        var expRemain = exp[..expTime.Index] + exp[(expTime.Index + expTime.Length)..];
        var actRemain = act[..actTime.Index] + act[(actTime.Index + actTime.Length)..];
        if (actRemain != expRemain) return result; // 如果除了时间以外还有其他不一样的，那么直接返回false
        
        // 对act产生的时间标记，做规范性检查。对齐到标准中的每一条
        if (actRemain.Contains('h'))
        { // Hold / TouchHold
            Assert.False(actTime.Groups[1].Success, $"Hold/TouchHold不应该有等待时间！{act}");
            if (actTime.Groups[4].Success)
            { // 绝对时长的情况
                Assert.True(act[actTime.Groups[4].Index - 1] == '#', $"Hold/TouchHold格式不正确，绝对时长的前面必须带一个井号！{act}");
            }
        }
        else
        {
            if (actTime.Groups[4].Success)
            { // 绝对时长的情况，前面必须是'bpm#'或'等待时间##'。我们不考虑前面一种情况，则应该断言一定是第二种情况出现了
                Assert.True(actTime.Groups[1].Success && actTime.Groups[1].Index + actTime.Groups[1].Length == actTime.Groups[4].Index, $"星星持续时长使用了非标准语法！{act}");
            }
        }
        
        var bpm = chart.BpmList.Find(time).Bpm;
        if (expTime.Groups[2].Success && actTime.Groups[4].Success)
        { // exp中是分数时间、act中是小数时间的情况
            // 分数时间化为小数时间，看是否对的上；分子/分母顺序与 VisitBeats 一致
            var sec = new Rational(int.Parse(expTime.Groups[3].Value), int.Parse(expTime.Groups[2].Value)) * (240 / (Rational)bpm);
            if (Near((double)sec, double.Parse(actTime.Groups[4].Value, CultureInfo.InvariantCulture), bpm)) result = true; // 如果对的上，则不判定为比较失败
        }
        else if (actTime.Groups[2].Success && expTime.Groups[4].Success)
        { // exp中是小数时间、act中是分数时间的情况
            // 分数时间化为小数时间，看是否对的上
            var sec = new Rational(int.Parse(actTime.Groups[3].Value), int.Parse(actTime.Groups[2].Value)) * (240 / (Rational)bpm);
            if (Near((double)sec, double.Parse(expTime.Groups[4].Value, CultureInfo.InvariantCulture), bpm)) result = true; // 如果对的上，则不判定为比较失败
        }
        else if (actTime.Groups[4].Success && expTime.Groups[4].Success)
        { // exp中是小数时间、act中是小数时间的情况
            var expSec = double.Parse(expTime.Groups[4].Value, CultureInfo.InvariantCulture);
            var actSec = double.Parse(actTime.Groups[4].Value, CultureInfo.InvariantCulture);
            if (Near(expSec, actSec, bpm)) result = true; // 如果对的上，则不判定为比较失败
        }
                
        // 比较等待时间是否相等（没显式写出的就是1拍）
        var expWait = expTime.Groups[1].Success ? double.Parse(expTime.Groups[1].Value, CultureInfo.InvariantCulture) : 60 / (double)bpm;
        var actWait = actTime.Groups[1].Success ? double.Parse(actTime.Groups[1].Value, CultureInfo.InvariantCulture) : 60 / (double)bpm;
        if (!Near(expWait, actWait, bpm)) result = false; // 如果等待时间对不上，则仍判定为比较失败
        return result;
    }

    private static string RearrangeNote(string s)
    {
        return string.Join('`', s.Split('`').Select(x =>
        {
            var t = x.Split('/');
            t.Sort();
            return string.Join('/', t);
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
