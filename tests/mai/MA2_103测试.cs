using System.Text;
using MuConvert.mai;
using MuConvert.utils;
using static MuConvert.Tests.mai.TestUtils;

namespace MuConvert.Tests.mai;

/// <summary>
/// 官谱中 golden MA2 头为 <c>1.03.00</c> 的谱面：Simai（lv5）→ <see cref="MA2_103Generator"/> 与对应 <c>*03.ma2</c> 音符段一致。
/// </summary>
public class MA2_103测试
{
    public static IEnumerable<object[]> Official103Lv5()
    {
        const int levelId = 5;
        var testsetRoot = Path.Combine(FindTestsetRoot().FullName, "官谱");
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

        AssertMa2NotesEqual(KeepNotesOnly(expectedMa2), KeepNotesOnly(ma2), input.ToString());
    }
}
