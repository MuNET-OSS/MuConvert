using System.Text;
using MuConvert.generator;
using MuConvert.parser;
using Xunit.Abstractions;
using static MuConvert.Tests.TestUtils;

namespace MuConvert.Tests;

/// <summary>
/// 官谱 MA2 → Chart（<see cref="MA2Parser"/>）→ MA2（<see cref="MA2Generator"/> / <see cref="MA2_103Generator"/>）轮回合后，
/// 与原版 MA2 末尾统计段（<c>T_REC_*</c> 起至文件结束）逐项一致。
/// </summary>
public class Statistics测试
{
    private readonly ITestOutputHelper _output;

    public Statistics测试(ITestOutputHelper output) => _output = output;

    /// <summary>8 张官谱各取 maidata 等级 5（对应 <c>*03.ma2</c>）。</summary>
    public static IEnumerable<object[]> OfficialLevel5()
    {
        const int levelId = 5;
        var repoRoot = FindRepoRoot();
        var testsetRoot = Path.Combine(repoRoot.FullName, "tests", "testset", "官谱");
        if (!Directory.Exists(testsetRoot))
            throw new DirectoryNotFoundException($"Testset root not found: {testsetRoot}");

        foreach (var maidataPath in Directory.EnumerateFiles(testsetRoot, "maidata.txt", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
            yield return [new TestInput(maidataPath, levelId)];
    }

    [Theory]
    [MemberData(nameof(OfficialLevel5))]
    public void 统计段与原版一致(TestInput input)
    {
        var ma2Original = File.ReadAllText(input.MA2, Encoding.UTF8);
        var (chart, parseAlerts) = new MA2Parser().Parse(ma2Original);
        Assert.Empty(parseAlerts);

        var ma2Version = TryParseMa2HeaderVersion(ma2Original);
        MA2Generator generator = ma2Version == 103 ? new MA2_103Generator() : new MA2Generator();
        var (ma2RoundTrip, genAlerts) = generator.Generate(chart);
        Assert.Empty(genAlerts);

        var expected = Ma2StatisticsSection.Parse(ma2Original);
        Assert.NotEmpty(expected);

        var actual = Ma2StatisticsSection.Parse(ma2RoundTrip);
        Ma2StatisticsSection.AssertEqual(expected, actual, input.ToString(), _output);
    }
}

internal static class Ma2StatisticsSection
{
    /// <summary>与 <see cref="MA2Generator"/> 当前实现尚未对齐的统计键，不参与等价断言。</summary>
    public static readonly HashSet<string> SkippedKeys =
    [
        "TTM_EACHPAIRS",
        "T_JUDGE_HLD",
        "T_JUDGE_ALL",
    ];

    /// <summary>从首个 <c>T_REC_</c> 行起解析到文件末尾，得到 <c>键 → 值</c>（不含键前缀）。</summary>
    public static Dictionary<string, string> Parse(string ma2Text)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var started = false;
        foreach (var raw in ma2Text.EnumerateLines())
        {
            var line = raw.ToString().TrimEnd('\r');
            if (!started)
            {
                if (line.StartsWith("T_REC_", StringComparison.Ordinal))
                    started = true;
                else
                    continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var tab = line.IndexOf('\t');
            if (tab <= 0)
                continue;

            dict[line[..tab]] = line[(tab + 1)..];
        }

        return dict;
    }

    public static void AssertEqual(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual,
        string context,
        ITestOutputHelper? output)
    {
        foreach (var kv in expected.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (SkippedKeys.Contains(kv.Key))
                continue;

            if (!actual.TryGetValue(kv.Key, out var actVal))
            {
                Assert.Fail($"{context}: 缺少统计键 {kv.Key}（expected {kv.Value}）");
            }

            if (!ValuesEqual(kv.Key, kv.Value, actVal))
            {
                Assert.Fail($"{context}: 统计键 {kv.Key} 不一致：expected {kv.Value}，actual {actVal}");
            }
        }

        foreach (var kv in actual)
        {
            if (SkippedKeys.Contains(kv.Key))
                continue;
            if (expected.ContainsKey(kv.Key))
                continue;
            Assert.Fail($"{context}: 多出原版没有的统计键 {kv.Key}={kv.Value}");
        }
    }

    private static bool ValuesEqual(string key, string expected, string actual)
    {
        return string.Equals(expected, actual, StringComparison.Ordinal);
    }
}
