using System.Text;
using MuConvert.mai;
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
        
        Assert.Equal(maidata.ClockCount * 96, TryParseMa2ClkDef(ma2));
        AssertMa2NotesEqual(KeepNotesOnly(expectedMa2), KeepNotesOnly(ma2), input.ToString());
        
        // 转出来的MA2，重新parse一次、确保没有任何错误
        var (_, alertsReparsed) = new MA2Parser().Parse(ma2);
        Assert.Empty(alertsReparsed);
    }
}
