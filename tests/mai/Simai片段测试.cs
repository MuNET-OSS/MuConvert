using System.Text;
using MuConvert.mai;
using Xunit.Abstractions;
using static MuConvert.Tests.mai.TestUtils;

namespace MuConvert.Tests.mai;

public class Simai片段测试
{
    private readonly ITestOutputHelper _output;

    public Simai片段测试(ITestOutputHelper output) => _output = output;
    public static IEnumerable<object[]> FragmentYamlFiles(string? filter = null)
    {
        var root = Path.Combine(FindTestsetRoot().FullName, "片段");
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"片段测例目录不存在: {root}");

        foreach (var path in Directory.EnumerateFiles(root, "*.yaml", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var seg = TestSegment.Load(path);
            Assert.True(seg.HasSimai || seg.HasMa2); // 至少要有一个，不然是无效测例
            var ok = filter switch
            {
                "both" => seg.HasSimai && seg.HasMa2,
                "simaiOnly" => seg.HasSimai && !seg.HasMa2,
                "ma2Only" => !seg.HasSimai && seg.HasMa2,
                _ => true
            };
            if (ok) yield return [seg];
        }
    }

    [Theory]
    [MemberData(nameof(FragmentYamlFiles), "both")]
    public void Simai片段转MA2(TestSegment c)
    {
        var (chart, parseAlerts) = new SimaiParser().Parse(c.Simai);
        var (ma2Full, genAlerts) = new MA2Generator(isUtage: false).Generate(chart);
        _output.WriteLine(string.Join('\n', parseAlerts));
        _output.WriteLine(string.Join('\n', genAlerts));

        var actual = KeepNotesOnly(ma2Full);
        var expected = NormalizeMa2Block(c.Ma2);
        AssertMa2NotesEqual(expected, actual, c.ToString());
    }
    
    [Theory]
    [MemberData(nameof(FragmentYamlFiles), "simaiOnly")]
    public void Simai_Roundtrip(TestSegment c)
    {
        var (chart, parseAlerts) = new SimaiParser().Parse(c.Simai);
        var (ma2Full, genAlerts) = new MA2Generator().Generate(chart);
        _output.WriteLine(string.Join('\n', parseAlerts));
        _output.WriteLine(string.Join('\n', genAlerts));
        
        var (chart2, parseAlerts2) = new MA2Parser().Parse(ma2Full);
        var (simaiRegenerated, genAlerts2) = new SimaiGenerator().Generate(chart2);
        _output.WriteLine(string.Join('\n', parseAlerts2));
        _output.WriteLine(string.Join('\n', genAlerts2));
        
        Assert.Equal(c.Simai.ReplaceLineEndings(""), simaiRegenerated.ReplaceLineEndings("")); // 暂时直接做字符串完全匹配，这样暂时是够用的。
        // AssertSimaiNotesEqual(c.Simai, simaiRegenerated, chart2, _output); // 如果之后不够用了。可以优先考虑开启这个
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
}
