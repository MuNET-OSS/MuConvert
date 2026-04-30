using System.Text;
using MuConvert.generator;
using MuConvert.maidata;
using MuConvert.parser;
using MuConvert.utils;
using Xunit.Abstractions;
using static MuConvert.Tests.mai.TestUtils;

namespace MuConvert.Tests.mai;

/* 都是让AI写的 */
public class Simai转MA2测试
{
    private readonly ITestOutputHelper _output;

    public Simai转MA2测试(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> GetTestInputs(string dataDir) => TestUtils.GetTestInputs(dataDir);
    
    [Theory]
    [MemberData(nameof(GetTestInputs), "自制谱")]
    public void 自制谱转MA2测试(TestInput c) => TestChart(c);
    
    private void TestChart(TestInput input)
    {
        var maidata = new Maidata(File.ReadAllText(input.Maidata, Encoding.UTF8));
        var chartInfo = maidata.Levels[input.LevelId];
        var expectedMa2 = File.ReadAllText(input.MA2, Encoding.UTF8);

        var (chart, alerts) = new SimaiParser(bigTouch: false, clockCount: maidata.ClockCount).Parse(chartInfo.Inote);
        var (ma2, alerts2) = new MA2Generator(isUtage: false).Generate(chart);
        _output.WriteLine(string.Join('\n', alerts));
        _output.WriteLine(string.Join('\n', alerts2));
        
        Assert.Equal(maidata.ClockCount * 96, TestUtils.TryParseMa2ClkDef(ma2));
        ma2 = KeepNotesOnly(ma2);
        expectedMa2 = KeepNotesOnly(expectedMa2);
        AssertTextEqual(expectedMa2, ma2);
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
                // 尝试同一时刻的其他行有无相同的，如果有，交换之
                var j = i + 1;
                while (j < expectedLines.Length && IsSameTime(expectedLines[i], actualLines[j]))
                {
                    if (CompareLine(expectedLines[j], act))
                    {
                        (expectedLines[j], expectedLines[i]) = (expectedLines[i], expectedLines[j]);
                        result = true;
                        break;
                    }
                    j++;
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
