using MuConvert.chart;
using Rationals;
using static MuConvert.Tests.TestUtils;

namespace MuConvert.Tests;

public class BPMList测试
{
    private static List<BPM> ToSecondsReference(BPMList bpmList)
    {
        // 复制 BPMList.ToSeconds 的实现（该方法是 private），用于对照测试
        List<BPM> result = [bpmList[0]];
        Assert.True(bpmList[0].Time == 0, "BPM列表的开头必须为0时刻");
        Rational accumulation = 0;
        for (int i = 1; i < bpmList.Count; i++)
        {
            accumulation += 240 / (Rational)bpmList[i - 1].Bpm * (bpmList[i].Time - bpmList[i - 1].Time);
            result.Add(new BPM(accumulation, bpmList[i].Bpm));
        }

        return result;
    }

    private static Rational ToSecondsReference(BPMList bpmList, Rational barTime)
    {
        var bpmIdx = bpmList.FindIndex(barTime);
        var secondsList = ToSecondsReference(bpmList);
        var seconds = secondsList[bpmIdx].Time;
        seconds += (barTime - bpmList[bpmIdx].Time) * (240 / (Rational)bpmList[bpmIdx].Bpm);
        return seconds;
    }

    [Fact]
    public void BPMList_ToSeconds函数测试()
    {
        var chart = LoadOneChart(out _);
        var bpms = chart.BpmList;

        // 固定挑选一组“看起来像随机但其实静态”的时间点：
        // - 0 与几个常见分拍
        // - 若干 BPM 切换点（从解析结果里取前几个），以及它们的 ±1/384 邻域（最容易出 off-by-one）
        var times = new List<Rational>
        {
            new(0),
            new(1, 384),
            new(1, 4),
            new(123456, 78910), // 一个稳定的非平凡分数（不依赖谱面内容）
        };

        // 取前 8 个 BPM 事件时刻（这张谱 BPM 变化很多；这里只取前缀，足够覆盖多段逻辑）
        foreach (var b in bpms.Take(8))
            times.Add(b.Time);

        foreach (var t in bpms.Take(8).Select(b => b.Time))
        {
            times.Add(t - new Rational(1, 384));
            times.Add(t + new Rational(1, 384));
        }

        foreach (var barTime in times)
        {
            if (barTime < 0) continue;

            var expected = ToSecondsReference(bpms, barTime);
            var actual = bpms.ToSecond(barTime);
            Assert.Equal(expected, actual);
        }
    }
}
