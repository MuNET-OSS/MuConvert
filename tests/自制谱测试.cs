using System.Text;
using MuConvert.generator;
using MuConvert.maidata;
using MuConvert.parser.simai;
using Xunit.Abstractions;

namespace MuConvert.Tests;

/* 都是让AI写的 */
public class 自制谱测试
{
    private readonly ITestOutputHelper _testOutputHelper;

    public 自制谱测试(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public record TestInput(string path, int levelId)
    {
        public override string ToString() => $"{Path.GetFileName(Path.GetDirectoryName(path))}-lv{levelId}";
    }

    /// <summary>
    /// 每个元素对应一张谱面：一条 Theory 用例只跑一个 chart。
    /// </summary>
    public static IEnumerable<object[]> AllCharts()
    {
        var repoRoot = FindRepoRoot();
        var testsetRoot = Path.Combine(repoRoot.FullName, "tests", "testset", "自制谱");
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
    [MemberData(nameof(AllCharts))]
    public void TestChart(TestInput input)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(input.path)!);

        var maidataTxt = File.ReadAllText(input.path, Encoding.UTF8);
        var maidata = new Maidata(maidataTxt);
        var chartInfo = maidata.Levels[input.levelId];

        var expectedSuffix = $"_{input.levelId - 2:D2}.ma2";
        var expected = dir.EnumerateFiles("*" + expectedSuffix, SearchOption.TopDirectoryOnly).ToList();
        Assert.True(expected.Count == 1,
            $"Expected exactly one golden file matching '*{expectedSuffix}' in '{dir.FullName}', got {expected.Count}.");

        var (chart, _) = new SimaiParser(bigTouch: false, isUtage: false).Parse(chartInfo.Inote);
        var (ma2, _) = new MA2Generator(clockCount: maidata.ClockCount).Generate(chart);

        var expectedMa2 = File.ReadAllText(expected[0].FullName, Encoding.UTF8);

        ma2 = keepNotesOnly(ma2);
        expectedMa2 = keepNotesOnly(expectedMa2);
        AssertTextEqual(expectedMa2, ma2);
    }

    private static string keepNotesOnly(string text)
    {
        var result = new StringBuilder();
        var METEncountered = false;
        foreach (var l in text.EnumerateLines())
        {
            var line = l.ToString();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("MET\t"))
            {
                METEncountered = true;
                continue;
            }
            if (!METEncountered) continue;
            if (line.StartsWith("T_REC")) break; // 结束
            result.Append(line + "\n");
        }
        return result.ToString();
    }

    private static void AssertTextEqual(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal)) return;

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var min = Math.Min(expectedLines.Length, actualLines.Length);

        var firstDiff = -1;
        for (var i = 0; i < min; i++)
        {
            if (!string.Equals(expectedLines[i], actualLines[i], StringComparison.Ordinal))
            {
                firstDiff = i;
                break;
            }
        }
        if (firstDiff == -1) firstDiff = min;

        var exp = firstDiff < expectedLines.Length ? expectedLines[firstDiff] : "<EOF>";
        var act = firstDiff < actualLines.Length ? actualLines[firstDiff] : "<EOF>";

        Assert.Fail(
            $"First difference at line {firstDiff + 1}:{Environment.NewLine}" +
            $"EXPECTED: {exp}{Environment.NewLine}" +
            $"ACTUAL  : {act}"
        );
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MuConvert.csproj")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("Could not locate repo root (MuConvert.csproj).");
    }
}
