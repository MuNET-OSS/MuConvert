using System.Globalization;
using System.Text;
using MuConvert.mai;
using MuConvert.utils;
using static MuConvert.Tests.mai.TestUtils;

namespace MuConvert.Tests.mai;

public class Simai片段测试
{
    public static IEnumerable<object[]> FragmentYamlFiles()
    {
        var root = Path.Combine(FindTestsetRoot().FullName, "片段");
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"片段测例目录不存在: {root}");

        foreach (var path in Directory.EnumerateFiles(root, "*.yaml", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.Ordinal))
            yield return [TestSegment.Load(path)];
    }

    [Theory]
    [MemberData(nameof(FragmentYamlFiles))]
    public void Simai片段转MA2(TestSegment c)
    {
        var (chart, parseAlerts) = new SimaiParser().Parse(c.Simai);
        var (ma2Full, genAlerts) = new MA2Generator(isUtage: false).Generate(chart);

        var actual = KeepNotesOnly(ma2Full);
        var expected = NormalizeMa2Block(c.Ma2);
        AssertMa2NotesEqual(expected, actual, c.ToString());
    }

    private static string NormalizeMa2Block(string text)
    {
        // 与生成结果一致：统一换行、去掉文末空行
        var sb = new StringBuilder();
        foreach (var l in text.EnumerateLines())
        {
            var line = l.ToString().TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) && sb.Length == 0)
                continue;
            sb.Append(line).Append('\n');
        }
        while (sb.Length > 0 && sb[^1] == '\n' && (sb.Length == 1 || sb[^2] == '\n'))
            sb.Length--;
        return sb.ToString().TrimEnd();
    }

    private static (int TimeTick, int Len, string Extra) GetSlideTime(string slide)
    {
        var values = slide.Split('\t');
        return (int.Parse(values[1], CultureInfo.InvariantCulture) * 384 + int.Parse(values[2], CultureInfo.InvariantCulture),
            int.Parse(values[5], CultureInfo.InvariantCulture),
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
