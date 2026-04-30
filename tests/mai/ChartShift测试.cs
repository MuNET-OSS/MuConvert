using MuConvert.chart;
using Rationals;
using static MuConvert.Tests.mai.TestUtils;

namespace MuConvert.Tests.mai;

/// <summary>
/// <see cref="Chart.Shift"/>：使用某官谱（SimaiParser 解析）作为真实谱面数据。
/// </summary>
public class ChartShift测试
{
    private static readonly Rational QuarterBar = new(1, 4);

    private static List<(Rational Time, int FalseEachIdx)> NotesInStableOrder(Chart c) =>
        c.Notes.OrderBy(n => n.Time).ThenBy(n => n.FalseEachIdx).Select(n => (n.Time, n.FalseEachIdx)).ToList();

    private static List<BPM> BpmInOrder(Chart c) => c.BpmList.OrderBy(x => x.Time).ToList();

    private static void AssertShiftResultTimelineValid(Chart c)
    {
        Assert.True(c.BpmList[0].Time == 0, "BPM 起点应在 0");
        foreach (var b in c.BpmList)
            Assert.True(b.Time >= 0);
        foreach (var n in c.Notes)
            Assert.True(n.Time >= 0);
    }

    /// <summary>
    /// 断言：after 相对 before 与 <paramref name="offset"/> 一致。
    /// 自 after 列表末尾向前与 before 尾部逐对比较；before 多出的前缀不参与成对比较，且其中每一项须满足 Time+offset&lt;0。
    /// 成对段：before 全局下标 0 的 BPM 时刻不变，其余 BPM 与各 note 时刻为对齐项加 offset。
    /// </summary>
    private static void AssertBpmAndNotesMatchPositiveShiftOffset(
        IReadOnlyList<BPM> bpmsBefore,
        IReadOnlyList<BPM> bpmsAfter,
        IReadOnlyList<(Rational Time, int FalseEachIdx)> notesBefore,
        IReadOnlyList<(Rational Time, int FalseEachIdx)> notesAfter,
        Rational offset)
    {
        if (offset >= 0)
        {
            Assert.Equal(bpmsBefore.Count, bpmsAfter.Count);
            Assert.Equal(notesBefore.Count, notesAfter.Count);
        }
        else
        {
            Assert.True(bpmsBefore.Count >= bpmsAfter.Count,
                $"BPM after ({bpmsAfter.Count}) 长于 before ({bpmsBefore.Count})，无法从尾部对齐比较。");
            Assert.True(notesBefore.Count >= notesAfter.Count,
                $"Notes after ({notesAfter.Count}) 长于 before ({notesBefore.Count})，无法从尾部对齐比较。");
        }

        for (var t = 0; t < bpmsAfter.Count; t++)
        {
            var iA = bpmsAfter.Count - 1 - t;
            var iB = bpmsBefore.Count - 1 - t;
            var bAfter = bpmsAfter[iA];
            var bBefore = bpmsBefore[iB];
            Assert.Equal(bBefore.Bpm, bAfter.Bpm);
            var expectedBpmTime = iA == 0 ? bBefore.Time : (bBefore.Time + offset).CanonicalForm;
            Assert.Equal(expectedBpmTime, bAfter.Time);
        }

        for (var t = 0; t < notesAfter.Count; t++)
        {
            var iA = notesAfter.Count - 1 - t;
            var iB = notesBefore.Count - 1 - t;
            var nAfter = notesAfter[iA];
            var nBefore = notesBefore[iB];
            Assert.Equal(nBefore.FalseEachIdx, nAfter.FalseEachIdx);
            Assert.Equal((nBefore.Time + offset).CanonicalForm, nAfter.Time);
        }

        var bpmPrefixLen = bpmsBefore.Count - bpmsAfter.Count;
        for (var i = 0; i < bpmPrefixLen; i++)
        {
            var shifted = (bpmsBefore[i].Time + offset).CanonicalForm;
            Assert.True(shifted < Rational.Zero,
                $"BPM 未对齐前缀下标 {i}: 要求 Time+offset&lt;0，实际为 {shifted}（Time={bpmsBefore[i].Time}, offset={offset}）。");
        }

        var notePrefixLen = notesBefore.Count - notesAfter.Count;
        for (var i = 0; i < notePrefixLen; i++)
        {
            var shifted = (notesBefore[i].Time + offset).CanonicalForm;
            Assert.True(shifted < Rational.Zero,
                $"Note 未对齐前缀下标 {i}: 要求 Time+offset&lt;0，实际为 {shifted}（Time={notesBefore[i].Time}, offset={offset}）。");
        }
    }

    [Fact]
    public void Shift_Zero()
    {
        var chart = LoadOneChart(out _);
        var notesBefore = NotesInStableOrder(chart);
        var bpmsBefore = BpmInOrder(chart);
        var startBpm = chart.StartBpm;

        chart.Shift(0);

        Assert.Equal(startBpm, chart.StartBpm);
        Assert.True(chart.BpmList[0].Time == 0, "BPM 起点应在 0");
        Assert.Equal(notesBefore, NotesInStableOrder(chart));
        Assert.Equal(bpmsBefore, BpmInOrder(chart));
    }

    [Fact]
    public void Shift_PositiveQuarterBar()
    {
        var chart = LoadOneChart(out _);
        var notesBefore = NotesInStableOrder(chart);
        var bpmsBefore = BpmInOrder(chart);

        chart.Shift(QuarterBar);

        AssertShiftResultTimelineValid(chart);
        AssertBpmAndNotesMatchPositiveShiftOffset(bpmsBefore, BpmInOrder(chart), notesBefore, NotesInStableOrder(chart), QuarterBar);
    }

    /// <summary>
    /// 负向 Shift：传入的 offset 为负（不变小节，以谱面开头 BPM 为基准），内部会先换成可变小节偏移再平移。
    /// </summary>
    [Fact]
    public void Shift_NegativeQuarterBar()
    {
        var chart = LoadOneChart(out _);
        var notesBefore = NotesInStableOrder(chart);
        var bpmsBefore = BpmInOrder(chart);
        var userOffset = -QuarterBar;

        chart.Shift(userOffset);

        AssertShiftResultTimelineValid(chart);
        AssertBpmAndNotesMatchPositiveShiftOffset(bpmsBefore, BpmInOrder(chart), notesBefore, NotesInStableOrder(chart), userOffset);
    }

    [Fact]
    public void Shift_PositiveThenNegativeQuarterBar_RoundTrip()
    {
        var chart = LoadOneChart(out _);
        var notesBefore = NotesInStableOrder(chart);
        var bpmsBefore = BpmInOrder(chart);

        chart.Shift(QuarterBar);
        AssertShiftResultTimelineValid(chart);
        chart.Shift(-QuarterBar);
        AssertShiftResultTimelineValid(chart);

        Assert.Equal(bpmsBefore, BpmInOrder(chart));
        Assert.Equal(notesBefore, NotesInStableOrder(chart));
    }

    [Fact]
    public void Shift_WithExplicitBpm_ScalesPositiveOffsetInBarSpace()
    {
        var chart = LoadOneChart(out _);
        var startBpm = chart.StartBpm;
        var halfBar = new Rational(1, 2);

        var firstBefore = chart.Notes.OrderBy(n => n.Time).ThenBy(n => n.FalseEachIdx).First().Time;

        chart.Shift(halfBar);
        var shiftedDefaultBpm = chart.Notes.OrderBy(n => n.Time).ThenBy(n => n.FalseEachIdx).First().Time;

        var chartScaled = LoadOneChart(out _);
        chartScaled.Shift(halfBar, bpm: startBpm * 2);
        var shiftedDoubleBpmArg = chartScaled.Notes.OrderBy(n => n.Time).ThenBy(n => n.FalseEachIdx).First().Time;

        Assert.Equal((firstBefore + halfBar).CanonicalForm, shiftedDefaultBpm);
        Assert.Equal((firstBefore + new Rational(1, 4)).CanonicalForm, shiftedDoubleBpmArg);
    }
}
