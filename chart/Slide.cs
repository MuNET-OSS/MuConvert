using System.Diagnostics;
using MuConvert.utils;
using Rationals;

namespace MuConvert.chart;

public class Star(Chart chart, Rational time) : Tap(chart, time);

/**
 * 一个Slide表示一整根星星，包括1-3-5-7这种分段fes星星。但不包括1-3*-5这种同头星星，同头星星会被表示成两个slide。
 * 对于同头星星，仅有第一根设置Head属性，后面的星星不设置Head属性但设置SharedHeadWith指向第一根。
 */
[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Slide : Note
{
    public Tap? Head; // 根据simai语法，星星头既可以是普通的星星形状(1-5)，也可以是Tap形状的(1@-5)，也可以没有(1?-5或1!-5)
    public Slide? SharedHeadWith;
    public List<SlideSegment> segments = new();
    public Duration WaitTime;

    public Slide(Chart chart, Rational time) : base(chart, time)
    {
       WaitTime = new Duration(this) { InvariantBar = (Rational)(1f / 4) };
    }

    public int StartKey
    {
        get => Head?.Key ?? SharedHeadWith?.StartKey ?? field;
        set
        {
            Utils.Assert(Head == null && SharedHeadWith?.StartKey == null, "尝试为有头星星手动设置星星头");
            field = value;
        }
    }
    
    public override Duration Duration
    {
        get
        {
            Duration? result = null;
            foreach (var s in segments)
            {
                if (s.Duration != null)
                {
                    if (result == null) result = s.Duration;
                    else result += s.Duration;
                }
            }

            return result ?? new Duration(this);
        }
        set
        {
            for (int i = 0; i < segments.Count - 1; i++)
            {
                segments[i].Duration = null;
            }
            segments.Last().Duration = value;
        }
    }

    private string DebuggerDisplay()
    {
        string result;
        if (SharedHeadWith != null) result = "*";
        else result = StartKey.ToString();
        if (Head != null && !(Head is Star)) result += "@"; // Tap形状的头
        else if (Head == null) result += "?"; // 无头

        var segStart = StartKey;
        foreach (var s in segments)
        {
            result += s.Type.ToSimai(segStart) + s.EndKey;
            if (s.Duration != null) result += s.Duration.DebuggerDisplay();
            segStart = s.EndKey;
        }

        result += Modifiers;
        return result;
    }
}

public class SlideSegment(Slide slide)
{
    public SlideType Type;
    public int EndKey;
    public Duration? Duration;
    
    public int StartKey
    {
        get
        {
            var idx = slide.segments.IndexOf(this);
            return idx > 0 ? slide.segments[idx-1].EndKey : slide.StartKey;
        }
    }
}

