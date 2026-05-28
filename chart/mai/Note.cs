using System.Diagnostics;
using MuConvert.chart;
using MuConvert.utils;
using Rationals;

namespace MuConvert.mai;

/**
 * maimai的Note基类
 */
public abstract class Note: BaseNote
{
    public readonly MaiChart Chart;
    protected int _key;
    
    public bool IsBreak;
    public bool IsEx;

    public int FalseEachIdx = 0; // 如果>0，表示这是一个伪双押，数字越大、延后的时刻越多

    public Rational TimeInSecond => Chart.ToSecond(Time);
    
    public virtual Duration Duration
    {
        get => new(this);
        set => throw new InvalidOperationException(Locale.NoDuration);
    }
    
    public virtual int Key
    {
        get => _key;
        set
        {
            if (value is < 1 or > 8) throw new ArgumentException(string.Format(Locale.InvalidKey, value));
            _key = value;
        }
    }

    protected Note(MaiChart chart, Rational time)
    {
        Chart = chart;
        Time = time;
    }
    
    public virtual string Modifiers => (IsBreak ? "b" : "") + (IsEx ? "x" : "");

    // 当前音符落在了哪些BPM区间内、分别有多长。
    public List<(int bpmIdx, decimal bpm, Rational start, Rational len)> BpmRanges =>
        StatisticsUtils.CalcBpmRanges(Time, EndTime, Chart);
    
    internal virtual string DebuggerDisplay() => "";

    public override Rational EndTime => Time + Duration.Bar;
}

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Tap(MaiChart chart, Rational time) : Note(chart, time)
{
    public Tap(Tap inTake): this(inTake.Chart, inTake.Time) // 拷贝构造函数
    {
        IsBreak = inTake.IsBreak;
        IsEx = inTake.IsEx;
        FalseEachIdx = inTake.FalseEachIdx;
        Key = inTake.Key;
    }

    internal override string DebuggerDisplay() => $"{Key}{Modifiers}";
}

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Hold : Tap
{
    public override Duration Duration { get; set; }

    public Hold(MaiChart chart, Rational time) : base(chart, time) { Duration = new Duration(this); }
    
    internal override string DebuggerDisplay() => $"{Key}h{Modifiers}{Duration.DebuggerDisplay()}";
}

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Touch(MaiChart chart, Rational time) : Note(chart, time)
{
    private TouchSeries _touchSeries;

    public bool IsFirework;
    public string TouchSize = chart.DefaultTouchSize;

    public string TouchArea
    {
        get => _touchSeries.ToString() + (Key == 0 ? "" : Key);
        set
        {
            if (value == "C") // 只有一个C字母的情况
            {
                _touchSeries = TouchSeries.C;
                _key = 0;
                return;
            }
            // 两个字符，字母+数字的情况
            if (value.Length != 2 || 
                !Enum.TryParse<TouchSeries>(value[..1], out var s) ||
                !int.TryParse(value[1..2], out var k) || 
                k < 1 || k > 8 || (s == TouchSeries.C && k > 2)
                ) throw new ArgumentException(string.Format(Locale.InvalidTouchArea, value));
            _touchSeries = s;
            _key = k;
        }
    }

    public override string Modifiers => base.Modifiers + (IsFirework ? "f" : "");
    
    internal override string DebuggerDisplay() => $"{TouchArea}{Modifiers}";
}

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class TouchHold : Touch
{
    public override Duration Duration { get; set; }

    public TouchHold(MaiChart chart, Rational time) : base(chart, time) { Duration = new Duration(this); }

    internal override string DebuggerDisplay() => $"{TouchArea}h{Modifiers}{Duration.DebuggerDisplay()}";
}

// 仅用于内部实现某些trick时使用的“伪音符”。用户在正常的谱面中是不会看到这个的。
internal class PseudoNote(MaiChart chart) : Note(chart, 0);
