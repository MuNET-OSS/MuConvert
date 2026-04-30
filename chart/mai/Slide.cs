using System.Diagnostics;
using MuConvert.utils;
using Rationals;

namespace MuConvert.mai;

public class Star : Tap
{
    public Star(MaiChart chart, Rational time): base(chart, time) {}
    
    public Star(Tap inTake): base(inTake) {} // 拷贝构造函数
}

/**
 * 一个Slide表示一整根星星，包括1-3-5-7这种分段fes星星。但不包括1-3*-5这种同头星星，同头星星会被表示成两个slide。
 * 对于同头星星，仅有第一根设置Head属性，后面的星星不设置Head属性但设置SharedHeadWith指向第一根。
 */
[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public class Slide : Note
{
    public Tap? OwnHead; // 属于自己的星星头。只有在SharedHeadWith=null的那根星星上应该设置此项，否则直接通过SharedHeadWith链过去即可
    // PS: 根据simai语法，星星头既可以是普通的星星形状(1-5)，也可以是Tap形状的(1@-5)，也可以没有(1?-5或1!-5)
    public Slide? SharedHeadWith;
    public List<SlideSegment> segments = new();
    public Duration WaitTime;

    public Slide(MaiChart chart, Rational time) : base(chart, time)
    {
       WaitTime = new Duration(this) { InvariantBar = new Rational(1, 4) };
    }

    public override int Key
    {
        get => OwnHead?.Key ?? SharedHeadWith?.Key ?? _key;
        set
        {
            Utils.Assert(OwnHead == null && SharedHeadWith?.Key == null, "尝试为有头星星手动设置星星头");
            if (value < 1 || value > 8) throw new ArgumentException(string.Format(Locale.InvalidKey, value));
            _key = value;
        }
    }
    
    // 所有的SharedHeadWith的连接关系，会构成一棵树，其中只有树根节点有OwnHead且SharedHeadWith为null。
    public Slide SharedHeadWithRoot => SharedHeadWith != null ? SharedHeadWith.SharedHeadWithRoot : this;

    public int EndKey => segments.Count > 0 ? segments.Last().EndKey : Key;
    
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

    internal override string DebuggerDisplay()
    {
        string result;
        if (SharedHeadWith != null) result = "*";
        else if (OwnHead != null)
        {
            result = OwnHead.DebuggerDisplay();
            if (!(OwnHead is Star)) result += "@"; // Tap形状的头
        }
        else result = Key + "?"; // 无头

        var segStart = Key;
        foreach (var s in segments)
        {
            result += s.Type.ToSimai(segStart) + s.EndKey;
            if (s.Duration != null) result += s.Duration.DebuggerDisplay();
            segStart = s.EndKey;
        }

        result += Modifiers;
        return result;
    }

    public override Rational EndTime => base.EndTime + WaitTime.Bar;
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
            return idx > 0 ? slide.segments[idx-1].EndKey : slide.Key;
        }
    }
}

