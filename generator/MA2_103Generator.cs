using MuConvert.chart;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.generator;

public class MA2_103Generator : MA2Generator
{
    public MA2_103Generator(bool isUtage = false): base(isUtage)
    {
        MA2Version = 103;
    }

    protected override MA2Line? AddTap(Tap tap, int bar, int tick)
    {
        var line = base.AddTap(tap, bar, tick);
        if (line == null) return null;
        var (mod, name) = (line.Name[..2], line.Name[2..5]);
        
        if (mod == "BX")
        { // 给个警告，还原为BR
            Warn(Locale.BreakExIn103, tap, line);
            mod = tap is not Hold ? "BR" : "NM";
        }
        if (mod == "BR")
        {
            if (tap is Star) name = "BST";
            else if (tap is Hold) Warn(Locale.BreakHoldOrSlideIn103, tap, line); // 给个警告。mod不用动，反正等会会忽略
            else name = "BRK";
        }
        else if (mod == "EX")
        {
            if (tap is Star) name = "XST";
            else if (tap is Hold) name = "XHO";
            else name = "XTP";
        }
        return line with { Name = name };
    }

    protected override List<MA2Line> AddSlide(Slide slide, int bar, int tick)
    {
        List<MA2Line> result = [];
        if (slide.OwnHead != null)
        {
            var headTap = AddTap(slide.OwnHead, bar, tick);
            if (headTap != null)
            {
                if (hasSameTimeTap(headTap))
                    alerts.Add(new Alert(Warning, Locale.SimultaneousSlideHead, (chart, slide.OwnHead.Time), null, slide.DebuggerDisplay()));
                else result.Add(headTap);
            }
        }
        
        var seg = slide.segments[0];
        var name = slide.segments[0].Type.ToString();
        var waitTime = T(slide.WaitTime.Bar, -slide.FalseEachIdx);
        var len = T(slide.Duration.Bar);
        var r = new MA2Line(name, bar, tick, seg.StartKey - 1, string.Join("\t", [waitTime, len, seg.EndKey - 1]));
        result.Add(r);

        if (slide.IsBreak) Warn(Locale.BreakHoldOrSlideIn103, slide, r);
        if (slide.segments.Count > 1) Warn(Locale.ConnectingSlideIn103, slide, r);
        return result;
    }

    protected override MA2Line? AddTouch(Touch touch, int bar, int tick)
    {
        var line = base.AddTouch(touch, bar, tick);
        if (line == null) return null;
        return line with { Name = line.Name[2..5] };
    }

    protected override Dictionary<string, string> statsNameConversion() => new()
    {
        ["TAP"] = "NMTAP", ["BRK"] = "BRTAP", ["XTP"] = "EXTAP",
        ["HLD"] = "NMHLD", ["XHO"] = "EXHLD",
        ["STR"] = "NMSTR", ["BST"] = "BRSTR", ["XST"] = "EXSTR",
        ["TTP"] = "NMTTP", ["THO"] = "NMTHO", ["SLD"] = "NMSLD",
    };
}