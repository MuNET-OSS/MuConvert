using System.Diagnostics;
using MuConvert.utils;
using Rationals;

namespace MuConvert.chart;

public abstract class Note
{
    public readonly Chart Chart;
    public Rational Time { get; set => field = value.CanonicalForm; }
    protected int _key;
    
    public bool IsBreak;
    public bool IsEx;

    public int FalseEachIdx = 0; // 如果>0，表示这是一个伪双押，数字越大、延后的时刻越多

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
            if (value < 1 || value > 8) throw new ArgumentException(string.Format(Locale.InvalidKey, value));
            _key = value;
        }
    }

    protected Note(Chart chart, Rational time)
    {
        Chart = chart;
        Time = time;
    }
    
    public virtual string Modifiers => (IsBreak ? "b" : "") + (IsEx ? "b" : "");
}

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Tap(Chart chart, Rational time) : Note(chart, time)
{
    public string DebuggerDisplay() => $"{Key}{Modifiers}";
}

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Hold : Tap
{
    public override Duration Duration { get; set; }

    public Hold(Chart chart, Rational time) : base(chart, time) { Duration = new Duration(this); }
    
    private string DebuggerDisplay() => $"{Key}h{Modifiers}{Duration.DebuggerDisplay()}";
}

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Touch(Chart chart, Rational time) : Note(chart, time)
{
    private TouchSeries _touchSeries;

    public bool IsFirework;
    public string TouchSize = chart.DefaultTouchSize;

    public string TouchArea
    {
        get => _touchSeries.ToString() + Key;
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
    
    private string DebuggerDisplay() => $"{TouchArea}{Modifiers}";
}

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class TouchHold : Touch
{
    public override Duration Duration { get; set; }

    public TouchHold(Chart chart, Rational time) : base(chart, time) { Duration = new Duration(this); }

    private string DebuggerDisplay() => $"{TouchArea}h{Modifiers}{Duration.DebuggerDisplay()}";
}
