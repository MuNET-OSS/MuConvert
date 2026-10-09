using System.Text;
using MuConvert.mai;
using Xunit.Abstractions;

namespace MuConvert.Tests.mai;

/// <summary>
/// 官谱 MA2 → Simai 与 maidata 中 inote 的比对：不逐字对比 Simai 文本，而是把双方按「顶层逗号」展开为
/// (Rational 时刻, 片段原文)，再比较时间轴（容忍分音写法不同导致的空白差异，以及少量 modifier 顺序差异）。
/// </summary>
public class MA2转Simai测试
{
    private readonly ITestOutputHelper _output;

    public MA2转Simai测试(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> GetTestInputs(string dataDir) => TestUtils.GetTestInputs(dataDir);
    
    [Theory]
    [MemberData(nameof(GetTestInputs), "官谱")]
    public void 官谱转Simai测试(TestInput c) => TestChart(c);
    
    private void TestChart(TestInput c)
    {
        var maidata = new Maidata(File.ReadAllText(c.Maidata, Encoding.UTF8));
        var inote = maidata.Levels[c.LevelId].Inote;
        var ma2Text = File.ReadAllText(c.MA2, Encoding.UTF8);
        
        var (chart, alerts) = new MA2Parser().Parse(ma2Text);
        var (simai, alerts2) = new SimaiGenerator().Generate(chart);
        _output.WriteLine(string.Join('\n', alerts));
        _output.WriteLine(string.Join('\n', alerts2));

        Assert.Equal(TestUtils.TryParseMa2ClkDef(ma2Text), chart.ClockCount * 96);
        TestUtils.AssertSimaiNotesEqual(inote, simai, chart, _output);
        
        // 转出来的simai，重新parse一次、确保没有任何错误
        var (_, alertsReparsed) = new SimaiParser(strictLevel: SimaiParser.StrictLevelEnum.Strict).Parse(simai);
        Assert.Empty(alertsReparsed);
    }
}
