using System.Text;
using System.Text.RegularExpressions;
using MuConvert.chart;
using MuConvert.maidata;
using MuConvert.parser;
using MuConvert.utils;
using YamlDotNet.Serialization;

namespace MuConvert.Tests;

internal static class TestUtils
{
    private static readonly Regex Ma2ClkDefLineRegex = new(@"^CLK_DEF\t(\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Ma2ClkLineRegex = new(@"^CLK\t(\d+)\s", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// 从测试运行目录向上查找包含 MuConvert.csproj 的仓库根目录。
    /// </summary>
    public static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MuConvert.csproj")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("Could not locate repo root (MuConvert.csproj).");
    }
    
    /// <summary>
    /// 自 MA2 文本中用正则匹配首行 <c>CLK_DEF\t…</c>（官机头字段名；部分资料误写为 CLOCK_DEF），返回其整数值；
    /// 与 <see cref="Chart.ClockCount"/> 的关系为 <c>CLK_DEF = 96 * ClockCount</c>（<c>RESOLUTION</c> 为 384 时）。
    /// </summary>
    public static int? TryParseMa2ClkDef(string ma2Text)
    {
        var m = Ma2ClkDefLineRegex.Match(ma2Text);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var v)) return null;
        if (v == 0)
        { // 对CLK_DEF为0的情况，就数一下CLK指令的个数，等效一下
            var rawClkLines = Ma2ClkLineRegex.Matches(ma2Text);
            v = rawClkLines.Count * 96;
        }
        return v;
    }

    /// <summary>解析 <c>VERSION</c> 行第三列（如 <c>1.03.00</c>）为整数版本号（如 103）；未找到则返回 <c>null</c>。</summary>
    public static int? TryParseMa2HeaderVersion(string ma2Text)
    {
        foreach (var raw in ma2Text.EnumerateLines())
        {
            if (raw.IsWhiteSpace()) continue;
            var line = raw.ToString().TrimEnd('\r');
            var parts = line.Split('\t');
            if (parts.Length < 3 || parts[0] != "VERSION")
                continue;
            var ver = parts[2].Split('.');
            if (ver.Length >= 2 &&
                int.TryParse(ver[0], out var major) &&
                int.TryParse(ver[1], out var minor))
                return major * 100 + minor;
            return null;
        }
        return null;
    }

    public static (int, int) ExtractMa2Time(string ma2Line)
    {
        var para = ma2Line.Split('\t');
        return (int.Parse(para[1]), int.Parse(para[2]));
    }

    public static bool IsSameTime(string ma2Line1, string ma2Line2) => ExtractMa2Time(ma2Line1) == ExtractMa2Time(ma2Line2);

    /// <summary>
    /// 提取 MA2 音符段至 <c>T_REC</c> 之前：跳过头部与 <c>BPM</c> 行；若存在 <c>MET\t</c> 小节行则跳过该行；
    /// 部分旧官谱 golden 无 <c>MET</c>，则在 <c>BPM</c> 块后的首条非头行开始收集。与 <see cref="Simai片段测试"/> / <see cref="MA2_103测试"/> 断言用逻辑一致。
    /// </summary>
    public static string KeepNotesOnly(string text)
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
                inNotes = true;
            }

            result.Append(line).Append('\n');
        }

        return result.ToString().Trim();
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
        line.StartsWith("BPM\t", StringComparison.Ordinal) ||
        line.StartsWith("MET\t", StringComparison.Ordinal) ||
        line.StartsWith("CLK\t", StringComparison.Ordinal);
    
    public static Chart LoadOneChart(out List<Alert> alerts)
    {
        var repo = FindRepoRoot();
        var maidataPath = Path.Combine(repo.FullName, "tests", "testset", "官谱", "Xaleid◆scopiX [DX]", "maidata.txt");
        Assert.True(File.Exists(maidataPath), $"Missing test maidata: {maidataPath}");

        var maidata = new Maidata(File.ReadAllText(maidataPath, Encoding.UTF8));
        Assert.True(maidata.Levels.ContainsKey(6), "Expected lv6 (inote_6) in maidata.");
        var chartInfo = maidata.Levels[6];

        var (chart, parseAlerts) = new SimaiParser(clockCount: maidata.ClockCount)
            .Parse(chartInfo.Inote);
        alerts = parseAlerts;
        chart.Sort();
        
        Assert.NotEmpty(chart.Notes);
        Assert.NotEmpty(chart.BpmList);
        Assert.True(chart.BpmList[0].Time == 0, "sanity");
        Assert.DoesNotContain(alerts, a => a.Level >= Alert.LEVEL.Error);
        return chart;
    }
    
    public static IEnumerable<object[]> GetTestInputs(string dataDir, int? lv = null, string? title = null)
    {
        var repoRoot = FindRepoRoot();
        var testsetRoot = Path.Combine(repoRoot.FullName, "tests", "testset", dataDir);
        if (!Directory.Exists(testsetRoot))
            throw new DirectoryNotFoundException($"Testset root not found: {testsetRoot}");

        foreach (var maidataPath in Directory.EnumerateFiles(testsetRoot, "maidata.txt", SearchOption.AllDirectories))
        {
            var maidataTxt = File.ReadAllText(maidataPath, Encoding.UTF8);
            var maidata = new Maidata(maidataTxt);
            foreach (var id in maidata.Levels.Keys.OrderBy(k => k))
            {
                // 如果指定了lv或title、但与要求不符，则不返回
                if ((lv != null && id != lv) || (title != null && !maidataPath.Contains("title"))) continue;
                yield return [new TestInput(maidataPath, id)];
            }
        }
    }
}

public sealed class TestSegment
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>YAML 文件的原始文件名（不含目录）。</summary>
    [YamlIgnore]
    public string YamlFileName { get; private set; } = "";
    [YamlMember(Alias = "simai")]
    public string Simai { get; set; } = "";
    [YamlMember(Alias = "ma2")]
    public string Ma2 { get; set; } = "";

    public override string ToString() => YamlFileName;

    public static TestSegment Load(string yamlPath)
    {
        var yamlFileName = Path.GetFileName(yamlPath);
        var text = File.ReadAllText(yamlPath, Encoding.UTF8);
        var seg = YamlDeserializer.Deserialize<TestSegment>(text)
                  ?? throw new FormatException($"{yamlPath}: 空 YAML 或根节点无法解析为映射");

        seg.YamlFileName = yamlFileName;
        seg.Simai = seg.Simai.Trim();
        seg.Ma2 = seg.Ma2.Trim();

        if (string.IsNullOrWhiteSpace(seg.Simai))
            throw new FormatException($"{yamlPath}: 缺少或为空 simai");
        if (string.IsNullOrWhiteSpace(seg.Ma2))
            throw new FormatException($"{yamlPath}: 缺少或为空 ma2");
        
        return seg;
    }
}

public record TestInput(string Maidata, int LevelId)
{
    public string Dir = Path.GetDirectoryName(Maidata)!;

    public string MA2
    {
        get
        {
            var expectedSuffix = $"{LevelId - 2:D2}.ma2";
            var dirInfo = new DirectoryInfo(Dir);
            var expected = dirInfo.EnumerateFiles("*" + expectedSuffix, SearchOption.TopDirectoryOnly).ToList();
            Assert.True(expected.Count == 1,
                $"Expected exactly one golden file matching '*{expectedSuffix}' in '{dirInfo.FullName}', got {expected.Count}.");
            return expected[0].FullName;
        }
    }
    
    public override string ToString() => $"{Path.GetFileName(Path.GetDirectoryName(Maidata))}-lv{LevelId}";
}
