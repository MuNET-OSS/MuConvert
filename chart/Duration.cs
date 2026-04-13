using System.Diagnostics;
using Rationals;

namespace MuConvert.chart;

/**
 * 用于表示持续时间的类，使用于hold和slide的持续时长以及slide的等待时长中。
 *
 * 本类底层存储的时间分为三种类型：
 * Bar (Fraction)：以小节为单位的时间，考虑持续期间BPM的变化。
 * InvariantBar (Fraction)：以小节为单位的时间，但不考虑持续期间BPM的变化、以关联的Note开始的那一刻的BPM作为基准。
 * Seconds (double)：以秒为单位的绝对时间。
 *
 * 更多的细节，请参见“开发者指南”的“关于时间格式”部分。
 */
[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Duration
{
    private enum Type 
    {
        Bar, // 以小节为单位，且考虑了当前谱面的BPM变化。从MA2中读出持续时间的就属于这一类。
        InvariantBar, // 以小节为单位，但是只根据当前音符开始位置的bpm来计算、无视这期间可能的bpm变化。
        Seconds, // 以绝对的秒为单位
    }
    
    private Type _type = Type.Seconds;
    private Rational _data = 0;
    private Note _note;
    
    public Duration(Note note)
    {
        _note = note;
    }

    private BPMList BpmList => _note.Chart.BpmList;

    public Rational Bar
    {
        get
        {
            switch (_type)
            {
                case Type.Bar:
                    return _data;
                case Type.InvariantBar:
                    var bpmIndex = BpmList.FindIndex(_note.Time);
                    var invariantBpm = BpmList[bpmIndex].Bpm; // 音符开始时刻的bpm是不变bpm
                    return ConvertTime(_data, (Rational)invariantBpm, null);
                case Type.Seconds:
                    return ConvertTime(_data, 240, null); // seconds秒数可以等效为240bpm下的小节数
                default:
                    throw new InvalidOperationException();
            }
        }
        set
        {
            _type = Type.Bar;
            _data = value.CanonicalForm;
        }
    }

    public Rational InvariantBar
    {
        get
        {
            switch (_type)
            {
                case Type.InvariantBar:
                    return _data;
                case Type.Bar:
                    var bpmIndex = BpmList.FindIndex(_note.Time);
                    var invariantBpm = BpmList[bpmIndex].Bpm; // 音符开始时刻的bpm是不变bpm
                    return ConvertTime(_data, null, (Rational)invariantBpm);
                case Type.Seconds:
                    bpmIndex = BpmList.FindIndex(_note.Time);
                    invariantBpm = BpmList[bpmIndex].Bpm; // 音符开始时刻的bpm是不变bpm
                    return ConvertTime(_data, 240, (Rational)invariantBpm); // seconds秒数可以等效为240bpm下的小节数
                default:
                    throw new InvalidOperationException();
            }
        }
        set
        {
            _type = Type.InvariantBar;
            _data = value.CanonicalForm;
        }
    }
    
    public Rational Seconds
    {
        get
        {
            switch (_type)
            {
                case Type.Seconds:
                    return _data;
                case Type.Bar:
                    return ConvertTime(_data, null, 240); // seconds秒数可以等效为240bpm下的小节数
                case Type.InvariantBar:
                    var bpmIndex = BpmList.FindIndex(_note.Time);
                    var invariantBpm = BpmList[bpmIndex].Bpm; // 音符开始时刻的bpm是不变bpm
                    return ConvertTime(_data, (Rational)invariantBpm, 240);
                default:
                    throw new InvalidOperationException();
            }
        }
        set
        {
            _type = Type.Seconds;
            _data = value.CanonicalForm;
        }
    }

    /**
     * 用于在不同格式的时间数值之间转换的函数。
     *
     * 首先指出一个重要原理：Seconds格式下以秒为单位的时间，在数值上实际等价于 240bpm下的不变小节时间。这就是为什么可以构造一个通用的转换函数的原理。
     *
     * <param name="value">要被转换的时间值</param>
     * <param name="srcBpm">指定value所对应的源bpm。若为None，表示使用BPMList中动态的bpm(对应把Bar转换为其他类型的情况)</param>
     * <param name="dstBpm">转换的目标bpm。若为None，表示使用BPMList中动态的bpm(对应把其他类型转换为Bar的情况)</param>
     */
    private Rational ConvertTime(Rational value, Rational? srcBpm, Rational? dstBpm)
    {
        var startTime = _note.Time;
        if (_note is Slide slide && slide.WaitTime != this)
        { // 如果我不是WaitTime，则我是Duration，则应加上等待时间
            startTime += slide.WaitTime.Bar;
        }
        return ConvertTime(startTime, value, srcBpm, dstBpm, BpmList);
    }
    
    public static Rational ConvertTime(Rational startTime, Rational value, Rational? srcBpm, Rational? dstBpm, BPMList bpmList)
    {
        if (srcBpm != null && dstBpm != null)
        {
            // 静态的src和dst，直接算一下即可，无需遍历bpm表
            return (value * (dstBpm.Value / srcBpm.Value)).CanonicalForm;
        }
        else
        {
            var rangeStart = startTime;
            var bpmIndex = bpmList.FindIndex(rangeStart);
            Rational result = 0;
            Rational remain = value;
            while (remain > 0)
            {
                // 当前所处bpm区间的结束位置。如果当前已经是最后一个区间了，则结束位置写成一个很大的数就可以了，反正本轮remain一定会被清空
                var bpmRangeEnd = bpmIndex < bpmList.Count - 1 ? bpmList[bpmIndex + 1].Time : 9999999;
                // 本区间可以消耗掉remain的最大数量，以src的bpm为单位。
                Rational curRangeCapacity = bpmRangeEnd - rangeStart;
                
                var srcBpmNow = srcBpm; // 每次循环要复制一份srcBpm，不然直接改了srcBpm的话，再次循环时逻辑就不对了
                var dstBpmNow = dstBpm;
                if (srcBpmNow == null)
                { // 如果srcBpm传入的是None，说明应该使用当前的实时bpm作为srcBpm
                    srcBpmNow = (Rational)bpmList[bpmIndex].Bpm;
                    // 此时capacity已经是以srcBpm为单位了，无需再转换
                }
                else if (dstBpmNow == null)
                {
                    dstBpmNow = (Rational)bpmList[bpmIndex].Bpm;
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
    
    public static Duration operator +(Duration a, Duration b)
    {
        var result = new Duration(a._note){_type = a._type, _data = a._data};
        switch (result._type)
        {
            case Type.Bar:
                result._data = (result._data + b.Bar).CanonicalForm;
                break;
            case Type.InvariantBar:
                result._data = (result._data + b.InvariantBar).CanonicalForm;
                break;
            case Type.Seconds:
                result._data = (result._data + b.Seconds).CanonicalForm;
                break;
        }

        return result;
    }

    public static Duration operator /(Duration a, int b)
    {
        return new Duration(a._note){_type = a._type, _data = (a._data / b).CanonicalForm};
    }
    
    public string DebuggerDisplay() => _type == Type.Seconds ? $"[#{(float)_data}]" : $"[{_data.Denominator}:{_data.Numerator}]";
}