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

    [Theory(Skip = "ogkr的解析和生成还没实现完，所以暂时跳过")]
    [MemberData(nameof(GetTestInputs), "官谱")]
    public void 解析Ogkr再生成回去(OgkrTestInput c)
    {
        var ogkrText = File.ReadAllText(c.OgkrPath, Encoding.UTF8);

        var (chart, parseAlerts) = new OgkrParser().Parse(ogkrText);
        var (resultText, generateAlerts) = new OgkrGenerator().Generate(chart);

        // 转出来的IR的基本健全性检查
        Assert.NotEmpty(chart.BpmList);
        Assert.True(chart.BpmList[0].Time == 0, "BpmList首项必须为0时刻");
        Assert.NotEmpty(chart.Notes);
        Assert.DoesNotContain(parseAlerts, a => a.Level == Alert.LEVEL.Error);
        
        _output.WriteLine(string.Join('\n', parseAlerts));
        _output.WriteLine(string.Join('\n', generateAlerts));
        
        OgkrTextComparer.AssertOgkrEqual(ogkrText, resultText);
    }
}

/// <summary>
/// 对两份 ogkr 文本进行“分段、逐行”的比较。
///
/// 段落与比较规则：
/// 1. [HEADER]：忽略 T_ 开头的统计量与 TUTORIAL，其余逐行严格比较。
/// 2. [B_PALETTE]：不直接逐行比较；但要求 actual 内不存在两行“实质相同”
///    （除去 ID 之外其余字段全部相同）。
/// 3. [COMPOSITION] / [LANE] / [LANE_BLOCK] / [BEAM] / [FLICK] / [NOTES]：逐行严格比较。
/// 4. [BULLET] / [BELL]：逐行比较，但其中引用 B_PALETTE ID 的字段，比较的是
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
        CompareSimpleSection("LANE_BLOCK", expectedSections, actualSections);
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
