using System.Text;
using MuConvert.generator;
using MuConvert.maidata;
using MuConvert.parser;
using MuConvert.utils;
using static MuConvert.Tests.TestUtils;

namespace MuConvert.Tests;

/// <summary>
/// 官谱中 golden MA2 头为 <c>1.03.00</c> 的谱面：Simai（lv5）→ <see cref="MA2_103Generator"/> 与对应 <c>*03.ma2</c> 音符段一致。
/// </summary>
public class MA2_103测试
{
    public static IEnumerable<object[]> Official103Lv5()
    {
        const int levelId = 5;
        var repoRoot = FindRepoRoot();
        var testsetRoot = Path.Combine(repoRoot.FullName, "tests", "testset", "官谱");
        if (!Directory.Exists(testsetRoot))
            throw new DirectoryNotFoundException($"Testset root not found: {testsetRoot}");

        foreach (var maidataPath in Directory.EnumerateFiles(testsetRoot, "maidata.txt", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var maidata = new Maidata(File.ReadAllText(maidataPath, Encoding.UTF8));
            if (!maidata.Levels.ContainsKey(levelId))
                continue;

            var input = new TestInput(maidataPath, levelId);
            var golden = File.ReadAllText(input.MA2, Encoding.UTF8);
            if (TryParseMa2HeaderVersion(golden) != 103)
                continue;

            yield return [input];
        }
    }

    [Theory]
    [MemberData(nameof(Official103Lv5))]
    public void Simai转MA2_103(TestInput input)
    {
        var maidata = new Maidata(File.ReadAllText(input.Maidata, Encoding.UTF8));
        var chartInfo = maidata.Levels[input.LevelId];
        var expectedMa2 = File.ReadAllText(input.MA2, Encoding.UTF8);

        var (chart, parseAlerts) = new SimaiParser(bigTouch: false, clockCount: maidata.ClockCount).Parse(chartInfo.Inote);
        Assert.DoesNotContain(parseAlerts, a => a.Level >= Alert.LEVEL.Error);

        var (ma2, genAlerts) = new MA2_103Generator(isUtage: false).Generate(chart);
        Assert.DoesNotContain(genAlerts, a => a.Level >= Alert.LEVEL.Error);

        ma2 = KeepNotesOnly(ma2);
        expectedMa2 = KeepNotesOnly(expectedMa2);
        AssertMa2NotesEqual(expectedMa2, ma2, input.ToString());
    }

    /// <summary>
    /// 提取音符段至 <c>T_REC</c> 之前：跳过头部与 <c>BPM</c> 行；若存在 <c>MET\t</c> 小节行则跳过该行；
    /// 部分旧官谱 golden 无 <c>MET</c>，则在 <c>BPM</c> 块后的首条非头行开始收集。
    /// </summary>
    private static string KeepNotesOnly(string text)
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
                if (line.StartsWith("MET\t", StringComparison.Ordinal)) continue;
                inNotes = true;
            }

            result.Append(line).Append('\n');
        }

        return result.ToString();
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
        line.StartsWith("BPM\t", StringComparison.Ordinal);

    private static (int TimeTick, int Len, string Extra) GetSlideTime(string slide)
    {
        var values = slide.Split('\t');
        return (int.Parse(values[1]) * 384 + int.Parse(values[2]),
            int.Parse(values[5]),
            string.Join("\t", values[0], values[3], values[4], values[6]));
    }

    private static bool CompareLine(string exp, string act)
    {
        var result = string.Equals(exp, act, StringComparison.Ordinal);
        if (!result && exp.Length >= 5 && act.Length >= 5 && exp[..5] == act[..5] && SlideTypeTool.IsSlide(exp[2..5]))
        {
            var (expTime, expLen, expExtra) = GetSlideTime(exp);
            var (actTime, actLen, actExtra) = GetSlideTime(act);
            if (expExtra != actExtra) return result;
            if (exp[..2] == "CN")
            {
                if (expTime + expLen == actTime + actLen || Math.Abs(expLen - actLen) <= 1) result = true;
            }
            else
            {
                if (expTime == actTime && Math.Abs(expLen - actLen) <= 1) result = true;
            }
        }

        return result;
    }

    private static void AssertMa2NotesEqual(string expected, string actual, string context)
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
                for (var j = 1; j < Math.Min(expectedLines.Length, i + 5); j++)
                {
                    if (CompareLine(expectedLines[j], act))
                    {
                        (expectedLines[j], expectedLines[i]) = (expectedLines[i], expectedLines[j]);
                        result = true;
                        break;
                    }
                }
            }

            if (!result)
            {
                Assert.Fail(
                    $"{context}: first difference at line {i + 1}:{Environment.NewLine}" +
                    $"EXPECTED: {exp}{Environment.NewLine}" +
                    $"ACTUAL  : {act}");
            }
        }
    }
}
