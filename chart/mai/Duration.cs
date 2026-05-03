using System.Diagnostics;
using MuConvert.chart;
using Rationals;

namespace MuConvert.mai;

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
    internal decimal InvariantBpm => BpmList[BpmList.FindIndex(_note.Time)].Bpm;

    public Rational Bar
    {
        get
        {
            switch (_type)
            {
                case Type.Bar:
                    return _data;
                case Type.InvariantBar:
                    return ConvertTime(_data, InvariantBpm, null);
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
                    return ConvertTime(_data, null, InvariantBpm);
                case Type.Seconds:
                    return ConvertTime(_data, 240, InvariantBpm); // seconds秒数可以等效为240bpm下的小节数
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
                    return ConvertTime(_data, InvariantBpm, 240);
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

    private Rational ConvertTime(Rational value, decimal? srcBpm, decimal? dstBpm)
    {
        var startTime = _note.Time;
        if (_note is Slide slide && slide.WaitTime != this)
        { // 如果我不是WaitTime，则我是Duration，则应加上等待时间
            startTime += slide.WaitTime.Bar;
        }
        return BpmList.ConvertTime(startTime, value, srcBpm, dstBpm);
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
    
    internal string DebuggerDisplay() => _type == Type.Seconds ? $"[#{(float)_data}]" : $"[{_data.Denominator}:{_data.Numerator}]";
}