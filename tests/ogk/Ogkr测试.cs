using System.Text;
using MuConvert.ogk;
using MuConvert.utils;
using Xunit.Abstractions;

namespace MuConvert.Tests.ogk;

public class Ogkr测试
{
    private readonly ITestOutputHelper _output;

    public Ogkr测试(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> GetTestInputs(string dataDir) => OgkrTestUtils.GetTestInputs(dataDir);
    
    public static IEnumerable<object[]> GetTestInputsOnlyLv3(string dataDir) => GetTestInputs(dataDir)
        .Where(x=>int.TryParse(((OgkrTestInput)x[0]).DifficultyId, out var id) && id == 3);

    [Theory]
    [MemberData(nameof(GetTestInputs), "官谱")]
    public void 解析Ogkr再生成回去(OgkrTestInput c)
    {
        var ogkrText = File.ReadAllText(c.OgkrPath, Encoding.UTF8);

        var (chart, parseAlerts) = new OgkrParser().Parse(ogkrText);
        var (resultText, generateAlerts) = new OgkrGenerator().Generate(chart);
        AssertOgkChartOk(chart, parseAlerts.Concat(generateAlerts));
        
        _output.WriteLine(string.Join('\n', parseAlerts));
        _output.WriteLine(string.Join('\n', generateAlerts));
        
        OgkrTextComparer.AssertOgkrEqual(ogkrText, resultText);
    }
    
    [Theory]
    [MemberData(nameof(GetTestInputsOnlyLv3), "官谱")]
    public void 物量统计测试(OgkrTestInput c)
    {
        var ogkrText = File.ReadAllText(c.OgkrPath, Encoding.UTF8);
        var (chart, parseAlerts) = new OgkrParser().Parse(ogkrText);
        AssertOgkChartOk(chart, parseAlerts);
        
        // 从HEADER段直接解析原谱面中标注的T_xxx的期望值
        var expected = ParseExpectedTValues(ogkrText);
        var actual = chart.CountNotes();
        
        // 收集所有不一致的项，统一报出，便于调试
        var diffs = new List<string>();
        foreach (var key in expected.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var a = actual.GetValueOrDefault(key, -1);
            if (a != expected[key]) diffs.Add($"  {key}: expected={expected[key]}, actual={a}");
        }
        Assert.True(diffs.Count == 0, $"物量统计与原谱不一致:{Environment.NewLine}{string.Join(Environment.NewLine, diffs)}");
    }

    private static Dictionary<string, int> ParseExpectedTValues(string text)
    {
        var result = new Dictionary<string, int>();
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var parts = rawLine.Trim().Split('\t');
            if (parts.Length >= 2 && parts[0].StartsWith("T_") && int.TryParse(parts[1], out var v))
                result[parts[0]] = v;
        }
        return result;
    }

    private void AssertOgkChartOk(OgkChart chart, IEnumerable<Alert> alerts)
    {
        // 转出来的IR的基本健全性检查
        Assert.NotEmpty(chart.BpmList);
        Assert.True(chart.BpmList[0].Time == 0, "BpmList首项必须为0时刻");
        Assert.NotEmpty(chart.Notes);
        Assert.DoesNotContain(alerts, a => a.Level == Alert.LEVEL.Error);
    }
}

/// <summary>
/// 对两份 ogkr 文本进行“分段、逐行”的比较。
///
/// 段落与比较规则：
/// 1. [HEADER]：忽略 T_ 开头的统计量与 TUTORIAL，其余逐行严格比较。
/// 2. [B_PALETTE]：不直接逐行比较；但要求 actual 内不存在两行“实质相同”
///    （除去 ID 之外其余字段全部相同）。
/// 3. [COMPOSITION] / [LANE] / [BEAM] / [FLICK] / [NOTES]：逐行严格比较。
/// 4. [LANE_BLOCK]：将每一行视为一个多重集元素进行比较（顺序不敏感）。
///    这是因为官谱中观察到不同谱面对同一时刻的多条LBK使用了不同的排序约定，
///    且其在游戏内并无实际顺序意义，故宽松比较以适应这一点。
/// 5. [BULLET] / [BELL]：逐行比较，但其中引用 B_PALETTE ID 的字段，比较的是
///    所引用的 BPL 行的实质内容（去 ID 后的其余字段）是否相同，而非 ID 字面相等。
///
/// 比较失败时，会打印差异所在 expected 中的行号（1-based）。
/// </summary>
internal static class OgkrTextComparer
{
    public sealed record LineEntry(int LineNumber, string Content);

    // 在 BLT/BEL 行中，引用 B_PALETTE strID 的字段位置（按 \t 分割后的索引）。
    // BLT: BLT strId tUnit tGrid xUnit BulletType  → 索引 1
    // BEL: BEL tUnit tGrid xUnit bulletPallete    → 索引 4
    private const int BltPaletteIndex = 1;
    private const int BelPaletteIndex = 4;

    public static void AssertOgkrEqual(string expectedText, string actualText)
    {
        var expectedSections = ParseSections(expectedText);
        var actualSections = ParseSections(actualText);

        CompareHeaderSection(expectedSections, actualSections);

        AssertNoSubstantiallyEqualBpalettes(actualSections);

        var expectedBplSubstance = BuildBplSubstanceMap(expectedSections);
        var actualBplSubstance = BuildBplSubstanceMap(actualSections);

        CompareSimpleSection("COMPOSITION", expectedSections, actualSections);
        CompareSimpleSection("LANE", expectedSections, actualSections);
        CompareUnorderedSection("LANE_BLOCK", expectedSections, actualSections);
        CompareSimpleSection("BEAM", expectedSections, actualSections);
        CompareSimpleSection("FLICK", expectedSections, actualSections);
        CompareSimpleSection("NOTES", expectedSections, actualSections);

        CompareBulletOrBellSection("BULLET", BltPaletteIndex,
            expectedSections, actualSections, expectedBplSubstance, actualBplSubstance);
        CompareBulletOrBellSection("BELL", BelPaletteIndex,
            expectedSections, actualSections, expectedBplSubstance, actualBplSubstance);
    }

    private static Dictionary<string, List<LineEntry>> ParseSections(string text)
    {
        var result = new Dictionary<string, List<LineEntry>>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string? current = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                current = trimmed[1..^1];
                if (!result.ContainsKey(current))
                    result[current] = [];
                continue;
            }

            if (current == null) continue;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            result[current].Add(new LineEntry(i + 1, raw.TrimEnd()));
        }

        return result;
    }

    private static void CompareHeaderSection(
        Dictionary<string, List<LineEntry>> expectedSections,
        Dictionary<string, List<LineEntry>> actualSections)
    {
        var expected = (expectedSections.TryGetValue("HEADER", out var le) ? le : [])
            .Where(l => !ShouldSkipHeaderLine(l.Content)).ToList();
        var actual = (actualSections.TryGetValue("HEADER", out var la) ? la : [])
            .Where(l => !ShouldSkipHeaderLine(l.Content)).ToList();

        CompareLinesStrict("HEADER", expected, actual);
    }

    private static bool ShouldSkipHeaderLine(string content)
    {
        var first = content.Split('\t', 2)[0].Trim();
        return first.StartsWith("T_") || first == "TUTORIAL";
    }

    private static void CompareSimpleSection(string section,
        Dictionary<string, List<LineEntry>> expectedSections,
        Dictionary<string, List<LineEntry>> actualSections)
    {
        var expected = expectedSections.TryGetValue(section, out var le) ? le : [];
        var actual = actualSections.TryGetValue(section, out var la) ? la : [];
        CompareLinesStrict(section, expected, actual);
    }

    /// <summary>
    /// 把两个section内的所有行作为multiset做比较：只关心是否同时存在相同的行集合（含重数），不关心顺序。
    /// </summary>
    private static void CompareUnorderedSection(string section,
        Dictionary<string, List<LineEntry>> expectedSections,
        Dictionary<string, List<LineEntry>> actualSections)
    {
        var expected = expectedSections.TryGetValue(section, out var le) ? le : [];
        var actual = actualSections.TryGetValue(section, out var la) ? la : [];

        var expectedCounts = expected.GroupBy(e => e.Content).ToDictionary(g => g.Key, g => g.Count());
        var actualCounts = actual.GroupBy(e => e.Content).ToDictionary(g => g.Key, g => g.Count());

        var diffs = new List<string>();
        foreach (var (k, ec) in expectedCounts)
        {
            actualCounts.TryGetValue(k, out var ac);
            if (ac != ec) diffs.Add($"  missing/uneven in actual (expected x{ec}, actual x{ac}): {k}");
        }
        foreach (var (k, ac) in actualCounts)
        {
            if (!expectedCounts.ContainsKey(k)) diffs.Add($"  unexpected in actual (x{ac}): {k}");
        }
        if (diffs.Count > 0)
        {
            Assert.Fail($"[{section}] section (unordered) mismatch:{Environment.NewLine}{string.Join(Environment.NewLine, diffs)}");
        }
    }

    private static void CompareLinesStrict(string section, List<LineEntry> expected, List<LineEntry> actual)
    {
        var max = Math.Max(expected.Count, actual.Count);
        for (var i = 0; i < max; i++)
        {
            var eOk = i < expected.Count;
            var aOk = i < actual.Count;
            var eStr = eOk ? expected[i].Content : "<EOF>";
            var aStr = aOk ? actual[i].Content : "<EOF>";
            if (eOk && aOk && eStr == aStr) continue;
            var lineNumDesc = eOk ? $"line {expected[i].LineNumber}" : "<EOF>";
            
            Assert.Fail(
                $"[{section}] section mismatch (at {lineNumDesc}):{Environment.NewLine}" +
                $"  EXPECTED: {eStr}{Environment.NewLine}" +
                $"  ACTUAL  : {aStr}");
        }
    }

    /// <summary>
    /// 计算一行 B_PALETTE 条目（BPL 行）的“实质内容”：去掉位于索引 1 的 strID 字段后，
    /// 其余所有以制表符分隔的字段拼接而成的字符串。
    /// </summary>
    private static string ExtractBplSubstance(string line)
    {
        var tokens = line.Split('\t');
        if (tokens.Length < 2) return line;
        var sub = new List<string>(tokens.Length - 1);
        for (var i = 0; i < tokens.Length; i++)
            if (i != 1) sub.Add(tokens[i]);
        return string.Join('\t', sub);
    }

    private static void AssertNoSubstantiallyEqualBpalettes(Dictionary<string, List<LineEntry>> sections)
    {
        if (!sections.TryGetValue("B_PALETTE", out var lines) || lines.Count == 0) return;

        var seen = new Dictionary<string, LineEntry>();
        foreach (var line in lines)
        {
            var substance = ExtractBplSubstance(line.Content);
            if (seen.TryGetValue(substance, out var prev))
            {
                Assert.Fail(
                    $"[B_PALETTE] actual 中存在实质相同的两行（除 ID 外其余字段完全一致）:{Environment.NewLine}" +
                    $"  line {prev.LineNumber}: {prev.Content}{Environment.NewLine}" +
                    $"  line {line.LineNumber}: {line.Content}");
            }

            seen[substance] = line;
        }
    }

    /// <summary>
    /// 构建 B_PALETTE 中 strID → 实质内容 的映射，用于 BLT/BEL 行做引用解析比较。
    /// </summary>
    private static Dictionary<string, string> BuildBplSubstanceMap(
        Dictionary<string, List<LineEntry>> sections)
    {
        var map = new Dictionary<string, string>();
        if (!sections.TryGetValue("B_PALETTE", out var lines)) return map;
        foreach (var line in lines)
        {
            var tokens = line.Content.Split('\t');
            if (tokens.Length < 2) continue;
            var id = tokens[1].Trim();
            if (id.Length == 0) continue;
            map[id] = ExtractBplSubstance(line.Content);
        }
        return map;
    }

    private static void CompareBulletOrBellSection(string section, int idPosition,
        Dictionary<string, List<LineEntry>> expectedSections,
        Dictionary<string, List<LineEntry>> actualSections,
        Dictionary<string, string> expectedBplSubstance,
        Dictionary<string, string> actualBplSubstance)
    {
        var expected = expectedSections.TryGetValue(section, out var le) ? le : [];
        var actual = actualSections.TryGetValue(section, out var la) ? la : [];

        var max = Math.Max(expected.Count, actual.Count);
        for (var i = 0; i < max; i++)
        {
            var eOk = i < expected.Count;
            var aOk = i < actual.Count;
            var eStr = eOk ? expected[i].Content : "<EOF>";
            var aStr = aOk ? actual[i].Content : "<EOF>";
            var lineNumDesc = eOk ? $"line {expected[i].LineNumber}" : "<EOF>";

            string MakeFailMsg(string? extra = null) =>
                $"[{section}] section mismatch (at {lineNumDesc}):{Environment.NewLine}" +
                $"  EXPECTED: {eStr}{Environment.NewLine}" +
                $"  ACTUAL  : {aStr}" +
                (extra is null ? "" : $"{Environment.NewLine}  {extra}");

            if (!eOk || !aOk)
            {
                Assert.Fail(MakeFailMsg());
                continue;
            }

            var eTokens = expected[i].Content.Split('\t');
            var aTokens = actual[i].Content.Split('\t');

            if (eTokens.Length != aTokens.Length)
            {
                Assert.Fail(MakeFailMsg("(token count differs)"));
                continue;
            }

            for (var k = 0; k < eTokens.Length; k++)
            {
                if (k == idPosition)
                {
                    if (!ReferencesEquivalent(eTokens[k], aTokens[k],
                            expectedBplSubstance, actualBplSubstance))
                    {
                        Assert.Fail(MakeFailMsg(
                            $"(B_PALETTE reference mismatch: expected '{eTokens[k]}' vs actual '{aTokens[k]}')"));
                        break;
                    }
                }
                else if (eTokens[k] != aTokens[k])
                {
                    Assert.Fail(MakeFailMsg($"(token #{k} differs: '{eTokens[k]}' vs '{aTokens[k]}')"));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 比较 BLT/BEL 行中位于“调色板引用”字段的两个 ID 是否等价。
    /// 若两边的 ID 都在各自的 B_PALETTE 中能查到，则以其实质内容（去 ID 后的字段）相等为准；
    /// 若两边都不是有效引用（如 BEL 行的 "--"），则按字面值比较；
    /// 否则视为不等价。
    /// </summary>
    private static bool ReferencesEquivalent(string eId, string aId,
        Dictionary<string, string> expectedBplSubstance,
        Dictionary<string, string> actualBplSubstance)
    {
        var eHas = expectedBplSubstance.TryGetValue(eId, out var eSub);
        var aHas = actualBplSubstance.TryGetValue(aId, out var aSub);
        if (eHas && aHas) return eSub == aSub;
        if (!eHas && !aHas) return eId == aId;
        return false;
    }
}
