using System.Diagnostics;
using MuConvert.utils;
using Rationals;

namespace MuConvert.chart;

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class BPMList : List<BPM>
{
    private List<BPM> ToSeconds()
    {
        List<BPM> result = [this[0]];
        Utils.Assert(this[0].Time == 0, "BPM列表的开头必须为0时刻");
        Rational accumulation = 0;
        for (int i = 1; i < Count; i++)
        {
            accumulation += 240 / (Rational)(decimal)this[i-1].Bpm * (this[i].Time - this[i-1].Time);
            result.Add(new BPM(accumulation, this[i].Bpm));
        }
        return result;
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

public record BPM(Rational Time, float Bpm);
