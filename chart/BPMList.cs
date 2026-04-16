using System.Diagnostics;
using MuConvert.utils;
using Rationals;

namespace MuConvert.chart;

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class BPMList : List<BPM>
{
    public BPMList() {}
    public BPMList(IEnumerable<BPM> bpms): base(bpms) {}

    public Rational ToSecond(Rational barTime)
    {
        Utils.Assert(this[0].Time == 0, "BPM列表的开头必须为0时刻");
        Rational accumulation = 0;
        for (int i = 0; i < Count; i++)
        {
            var bpmRangeEnd = i < Count - 1 ? this[i + 1].Time : 999999;
            accumulation += 240 / (Rational)this[i].Bpm * (Utils.Min(barTime, bpmRangeEnd) - this[i].Time);
            if (barTime <= bpmRangeEnd) break;
        }
        return accumulation;
    }
    
    public int FindIndex(Rational time)
    {
        var i = 0;
        for (; i < Count; i++)
        {
            if (this[i].Time > time) break;
        }
        return i-1;
    }
    
    public BPM Find(Rational time) => this[FindIndex(time)];

    public bool IsBpmChanged(Rational start, Rational end)
    {
        var bpmIdx = FindIndex(start);
        return bpmIdx < Count - 1 && this[bpmIdx + 1].Time < end;
    }

    private string DebuggerDisplay()
    {
        var result = "";
        foreach (var bpm in this)
        {
            result += $"{bpm.Time:W}:{bpm.Bpm}; ";
        }

        return result;
    }
}

public record BPM(Rational Time, decimal Bpm);
