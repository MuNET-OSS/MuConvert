using System.Diagnostics;
using MuConvert.utils;
using Rationals;

namespace MuConvert.chart;

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class BPMList : List<BPM>
{
    public BPMList() {}
    public BPMList(IEnumerable<BPM> bpms): base(bpms) {}

    public Rational ToSecond(Rational barTime) => ConvertTime(0, barTime, null, 240);
    
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
    
    /**
     * 用于在不同格式的时间数值之间换算的函数。
     * 把从startTime起、长为value的一段分数时间，从srcBpm下换算到dstBpm下。
     *
     * 首先指出一个重要原理：Seconds格式下以秒为单位的时间，在数值上实际等价于 240bpm下的不变小节时间。这就是为什么可以构造一个通用的转换函数的原理。
     *
     * <param name="value">要被转换的时间值</param>
     * <param name="srcBpm">value原始值所基准的bpm。若为null，表示使用BPMList中动态的bpm(对应把Bar转换为其他类型的情况)</param>
     * <param name="dstBpm">想要换算到的目标bpm。若为null，表示使用BPMList中动态的bpm(对应把其他类型转换为Bar的情况)</param>
     */
    internal Rational ConvertTime(Rational startTime, Rational value, decimal? srcBpm, decimal? dstBpm)
    {
        Rational? srcBpmR = srcBpm != null ? (Rational?)srcBpm : null;
        Rational? dstBpmR = dstBpm != null ? (Rational?)dstBpm : null;
        if (srcBpmR != null && dstBpmR != null)
        {
            // 静态的src和dst，直接算一下即可，无需遍历bpm表
            return (value * (dstBpmR.Value / srcBpmR.Value)).CanonicalForm;
        }
        else
        {
            var rangeStart = startTime;
            var bpmIndex = FindIndex(rangeStart);
            Utils.Assert(bpmIndex >= 0, "startTime不应该在BPM表的范围之外！是否是BPM表没有以0时刻为开头造成的？");
            Rational result = 0;
            Rational remain = value;
            while (remain > 0)
            {
                // 当前所处bpm区间的结束位置。如果当前已经是最后一个区间了，则结束位置写成一个很大的数就可以了，反正本轮remain一定会被清空
                var bpmRangeEnd = bpmIndex < Count - 1 ? this[bpmIndex + 1].Time : 9999999;
                // 本区间可以消耗掉remain的最大数量，以src的bpm为单位。
                Rational curRangeCapacity = bpmRangeEnd - rangeStart;
                
                var srcBpmNow = srcBpmR; // 每次循环要复制一份srcBpm，不然直接改了srcBpm的话，再次循环时逻辑就不对了
                var dstBpmNow = dstBpmR;
                if (srcBpmNow == null)
                { // 如果srcBpm传入的是None，说明应该使用当前的实时bpm作为srcBpm
                    srcBpmNow = (Rational)this[bpmIndex].Bpm;
                    // 此时capacity已经是以srcBpm为单位了，无需再转换
                }
                else if (dstBpmNow == null)
                {
                    dstBpmNow = (Rational)this[bpmIndex].Bpm;
                    // 此时capacity是基于可变bpm即dstBpm的，需要换算到srcBpm上
                    curRangeCapacity *= (srcBpmNow.Value / dstBpmNow.Value);
                }

                Rational toSubtract = curRangeCapacity < remain ? curRangeCapacity : remain; // 要从remain中减掉的量，应该是（剩余量，本bpm区间允许消耗量）的最小值
                remain -= toSubtract;
                result += toSubtract * (dstBpmNow!.Value / srcBpmNow.Value);
                
                bpmIndex += 1;
                rangeStart = bpmRangeEnd;
            }

            return result.CanonicalForm;
        }
    }
}

public record BPM(Rational Time, decimal Bpm);

/**
 * 表示拍号的结构体。
 * 遵循英文的表达习惯，Numerator是每小节几拍，Denominator是每几分音符为一拍。
 * 注意MA2、C2S等格式中，MET的格式是Bar Tick Denominator Numerator，和常见顺序是反过来的。
 */
public record MET(Rational Time, int Numerator, int Denominator);
