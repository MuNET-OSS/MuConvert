using System.Text;
using MuConvert.chart;
using MuConvert.maidata;
using MuConvert.parser;
using MuConvert.utils;

namespace MuConvert.Tests;

internal static class TestUtils
{
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
