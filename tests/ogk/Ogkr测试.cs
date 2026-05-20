using System.Text;
using MuConvert.ogk;
using Xunit.Abstractions;

namespace MuConvert.Tests.ogk;

public class Ogkr测试
{
    private readonly ITestOutputHelper _output;

    public Ogkr测试(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> GetTestInputs(string dataDir) => OgkrTestUtils.GetTestInputs(dataDir);

    [Theory]
    [MemberData(nameof(GetTestInputs), "官谱")]
    public void 解析Ogkr再生成回去(OgkrTestInput c)
    {
        var ogkrText = File.ReadAllText(c.OgkrPath, Encoding.UTF8);

        var (chart, parseAlerts) = new OgkrParser().Parse(ogkrText);
        var (resultText, generateAlerts) = new OgkrGenerator().Generate(chart);

        _output.WriteLine(string.Join('\n', parseAlerts));
        _output.WriteLine(string.Join('\n', generateAlerts));
        
        Assert.Equal(ogkrText, resultText);
    }
}
