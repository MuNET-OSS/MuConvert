using System.Text;
using MuConvert.generator;
using MuConvert.maidata;
using MuConvert.parser.simai;
using MuConvert.utils;
using Xunit.Abstractions;
using static MuConvert.Tests.TestUtils;

namespace MuConvert.Tests;

/* 都是让AI写的 */
public class 自制谱测试
{
    private readonly ITestOutputHelper _output;

    public 自制谱测试(ITestOutputHelper output) => _output = output;

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
        var maidata = new Maidata(File.ReadAllText(input.Maidata, Encoding.UTF8));
        var chartInfo = maidata.Levels[input.LevelId];
        var expectedMa2 = File.ReadAllText(input.MA2, Encoding.UTF8);

        var (chart, _) = new SimaiParser(bigTouch: false, isUtage: false, clockCount: maidata.ClockCount).Parse(chartInfo.Inote);
        var (ma2, _) = new MA2Generator().Generate(chart);


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

    private static (int, int, string) GetSlideTime(string slide)
    {
        var values = slide.Split("\t");
        return (int.Parse(values[1]) * 384 + int.Parse(values[2]), int.Parse(values[5]), 
            string.Join("\t", values[0], values[3], values[4], values[6]));
    }

    private static bool CompareLine(string exp, string act)
    {
        var result = string.Equals(exp, act, StringComparison.Ordinal);
        if (!result && exp[..5] == act[..5] && SlideTypeTool.IsSlide(exp[2..5]))
        { // 如果是星星，则允许一定范围的误差。具体而言：
            var (expTime, expLen, expExtra) = GetSlideTime(exp);
            var (actTime, actLen, actExtra) = GetSlideTime(act);
            if (expExtra != actExtra) return result; // 首先任何情况下，waitTime和按键等信息必须相等
            if (exp[..2] == "CN")
            { // CN星星则要么尾时刻完全对，要么长度至多差1。
                if (expTime + expLen == actTime + actLen || Math.Abs(expLen - actLen) <= 1) result = true;
            }
            else
            { // 第一段星星则开始时刻必须对且长度至多差1
                if (expTime == actTime && Math.Abs(expLen - actLen) <= 1) result = true;
            }
        }
        return result;
    }

    private static void AssertTextEqual(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var max = Math.Max(expectedLines.Length, actualLines.Length);

        for (var i = 0; i < max; i++)
        {
            var exp = i < expectedLines.Length ? expectedLines[i] : "<EOF>";
            var act = i < actualLines.Length ? actualLines[i] : "<EOF>";
            var result = CompareLine(exp, act);
            if (!result)
            {
                // 尝试下面五行之内有无相同的，如果有，交换之
                for (int j = 1; j < Math.Min(expectedLines.Length, i+5); j++)
                {
                    if (CompareLine(expectedLines[j], act))
                    {
                        (expectedLines[j], expectedLines[i]) = (expectedLines[i], expectedLines[j]);
                        result = true;
                        break;
                    }
                }
            }

            if (!result) Assert.Fail(
                $"First difference at line {i + 1}:{Environment.NewLine}" +
                $"EXPECTED: {exp}{Environment.NewLine}" +
                $"ACTUAL  : {act}"
            );
        }
    }
}
