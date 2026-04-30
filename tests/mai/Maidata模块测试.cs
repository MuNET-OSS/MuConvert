using System.Text;
using MuConvert.collection;
using static MuConvert.Tests.mai.TestUtils;

namespace MuConvert.Tests.mai;

/// <summary>
/// Maidata 读写与 ToString 往返：testset 中的 maidata.txt 为输入。
/// 说明：ToString 会追加 ChartConvertTool 元数据，且键顺序与原始文件可能不同，故不做整文件逐字相等；
/// 往返后应得到与原字典相同的条目（另加工具键）。
/// </summary>
public class Maidata模块测试
{
    private const string ChartConvertToolKey = "ChartConvertTool";
    private const string ChartConvertToolVersionKey = "ChartConvertToolVersion";

    public static IEnumerable<object[]> MaidataFiles()
    {
        var dir = Path.Combine(FindTestsetRoot().FullName, "自制谱");
        string[] TO_TEST_CHARTS = ["Pre-STAR", "蝴蝶", "流光（Light Me Up）"];
        foreach (var chart in TO_TEST_CHARTS)
        {
            var path = Path.Combine(dir, chart, "maidata.txt");
            if (!File.Exists(path)) throw new FileNotFoundException($"Testset not found: {path}");
            yield return [path];
        }
    }

    [Theory]
    [MemberData(nameof(MaidataFiles))]
    public void 读入为Maidata_再ToString写出(string path)
    {
        var originalText = File.ReadAllText(path, Encoding.UTF8);
        var m = new Maidata(originalText);

        Assert.False(string.IsNullOrWhiteSpace(m.Title));
        Assert.False(string.IsNullOrWhiteSpace(m.Artist));
        Assert.NotEmpty(m.Levels);

        foreach (var (id, chart) in m.Levels.OrderBy(x => x.Key))
        {
            Assert.False(string.IsNullOrWhiteSpace(chart.Level), $"lv_{id} should be set in {path}");
            Assert.False(string.IsNullOrWhiteSpace(chart.Inote), $"inote_{id} should be non-empty in {path}");
        }

        Assert.True(m.First >= -10f && m.First <= 10f, $"sanity: first in reasonable range for {path}");
        Assert.True(m.ClockCount >= 1 && m.ClockCount <= 192, $"sanity: clock_count for {path}");

        var folder = Path.GetFileName(Path.GetDirectoryName(path))!;
        Assert.Equal(folder, m.Title);

        var serialized = m.ToString();
        var m2 = new Maidata(serialized);
        m2.AddToolData();
        AssertMaidataDictionaryEquivalent(m, m2);

        Assert.True(m2.ContainsKey(ChartConvertToolKey));
        Assert.Equal("MuConvert", m2[ChartConvertToolKey]);
        Assert.True(m2.ContainsKey(ChartConvertToolVersionKey));
        Assert.False(string.IsNullOrWhiteSpace(m2[ChartConvertToolVersionKey]));
    }

    private static void AssertMaidataDictionaryEquivalent(Maidata original, Maidata roundTrip)
    {
        foreach (var kv in original)
            Assert.Equal(kv.Value, roundTrip[kv.Key]);

        foreach (var k in roundTrip.Keys)
        {
            if (k is ChartConvertToolKey or ChartConvertToolVersionKey)
                continue;
            Assert.True(original.ContainsKey(k), $"Unexpected key in round-trip: {k}");
        }
    }
}
