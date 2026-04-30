using System.Text.RegularExpressions;
using MuConvert.chart;
using MuConvert.chart.mai;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.parser;

public class MA2Parser : IParser<MaiChart>
{
    private readonly MaiChart chart = new();
    private readonly List<Alert> alerts = [];

    private bool bpmRead = false;
    private int RSL = 384;
    public int MA2Version;
    
    private Dictionary<(Rational, int), List<Slide>> _slides = new();
    
    // 断言当前字段在header内（也就是还没遇到过bpm），否则报错
    private bool AssertInHeader(int lineNo, ReadOnlySpan<char> line)
    {
        if (bpmRead) Fail(Locale.InvalidMA2HeaderSentenceAfterHeader, lineNo, line);
        return true;
    }

    private void Fail(string reason, int lineNo, ReadOnlySpan<char> line)
    {
        alerts.Add(new Alert(Error, reason, lineNo, line.ToString()));
        throw new ConversionException(alerts);
    }

    private void WarnParamsCount(int lineNo, ReadOnlySpan<char> line, Rational? time)
    {
        Alert alert;
        if (time != null) alert = new Alert(Warning, Locale.MA2NoteSentenceTooManyParam, (chart, time.Value), lineNo, line.ToString());
        else alert = new Alert(Warning, Locale.MA2NoteSentenceTooManyParam, lineNo, line.ToString());
        alerts.Add(alert);
    }

    private Dictionary<string, string> Cmd103 = new()
    {
        ["BRK"] = "BRTAP", ["XTP"] = "EXTAP", ["XHO"] = "EXHLD", ["BST"] = "BRSTR", ["XST"] = "EXSTR"
    };
    
    public (MaiChart, List<Alert>) Parse(string text)
    {
        if (bpmRead) throw new Exception(Locale.InstanceMultipleUsage);

        int lineNo = 0;
        foreach (var line in text.EnumerateLines())
        {
            lineNo++;
            if (line.IsWhiteSpace()) continue;
            var values = line.ToString().Split('\t');
            var cmd = values[0];
            
            // 头字段
            if (cmd == "VERSION" && AssertInHeader(lineNo, line))
            {
                var version = Regex.Match(values[2], @"1\.(0?[2-5])\.(\d+)");
                MA2Version = 100 + int.Parse(version.Groups[1].Value);
                if (!version.Success) Fail(Locale.UnsuppoertedMA2Version, lineNo, line);
                else if (!(MA2Version is 103 or 104 or 105 && int.Parse(version.Groups[2].Value) == 0))
                    alerts.Add(new Alert(Warning, string.Format(Locale.WarnNonStdMA2Version, values[2]), line: lineNo, relevantNote: line.ToString()));
            }
            else if (cmd == "COMPATIBLE_CODE" && AssertInHeader(lineNo, line))
            {
                if (values[1] != "MA2") Fail(Locale.UnsuppoertedMA2Version,  lineNo, line);
            }
            else if (cmd is "FES_MODE" or "MET_DEF" or "BPM_DEF" or "GENERATED_BY" && AssertInHeader(lineNo, line)) {} // 这些不用解析，无事可做
            else if (cmd == "RESOLUTION" && AssertInHeader(lineNo, line)) 
                RSL = int.Parse(values[1]);
            else if (cmd == "CLK_DEF" && AssertInHeader(lineNo, line))
                chart.ClockCount = int.Parse(values[1]) / (RSL / 4);
            // BPM和MET
            else if (cmd == "BPM" && values.Length == 4 && int.TryParse(values[1], out var bbar) && 
                     int.TryParse(values[2], out var btick) && decimal.TryParse(values[3], out var bpm))
            {
                bpmRead = true;
                var time = bbar + new Rational(btick, RSL);
                chart.BpmList.Add(new BPM(time.CanonicalForm, bpm));
            }
            else if (cmd == "MET" && values.Length == 5 && int.TryParse(values[1], out var _) && int.TryParse(values[2], out var _) 
                     && int.TryParse(values[3], out var _) && int.TryParse(values[4], out var _)) {} // MET不需要解析，忽略之
            else if (cmd == "SEF") {} // SEF不需要解析，忽略之
            else if (cmd == "CLK" && values.Length == 3 && int.TryParse(values[1], out var clkBar) && int.TryParse(values[2], out var clkTick))
            { // CLK指令，现在就是添加到Chart.ExplicitClocks即可。避免“不认识的指令报错”，同时作为ClockCount的override
                chart.ExplicitClocks ??= [];
                chart.ExplicitClocks.Add(clkBar + new Rational(clkTick, RSL));
            } 
            // 读到了统计段，后面就不用读了，谱面解析结束
            else if (cmd.StartsWith("T_REC") || cmd.StartsWith("T_NUM") || cmd.StartsWith("T_JUDGE") || cmd.StartsWith("TTM_"))
            {
                break;
            }
            // 那么余下的情况，先按照是音符处理了，如果不行的话再报错
            else if (cmd.Length is 3 or 5 && values.Length >= 4 && int.TryParse(values[1], out var bar) && 
                     int.TryParse(values[2], out var tick) && int.TryParse(values[3], out var key))
            {
                key++;
                Note? note = null;
                Rational time = bar + new Rational(tick, RSL);
                string cc = cmd.Length == 3 ? Cmd103.GetValueOrDefault(cmd, cmd) : cmd, md = "";
                if (cc.Length == 5) (cc, md) = (cc[2..5], cc[..2]);
                
                int len;
                if (cc is "TAP" or "STR")
                {
                    note = cc == "STR" ? new Star(chart, time) : new Tap(chart, time);
                    note.Key = key;
                    if (values.Length != 4) WarnParamsCount(lineNo, line, time);
                }
                else if (cc == "HLD" && values.Length >= 5 && int.TryParse(values[4], out len))
                {
                    note = new Hold(chart, time) { Key = key };
                    var duration = new Duration(note) { Bar = new Rational(len, RSL) };
                    note.Duration = duration;
                    if (values.Length != 5) WarnParamsCount(lineNo, line, time);
                }
                else if (cc == "TTP" && values.Length >= 6)
                {
                    var touch = new Touch(chart, time) { TouchArea = GetTouchArea(values[4], key), IsFirework = values[5] == "1"};
                    note = touch;
                    if (values.Length >= 7) touch.TouchSize = values[6];
                    if (!(values.Length == 7 || (values.Length == 6 && MA2Version == 102))) WarnParamsCount(lineNo, line, time);
                }
                else if (cc == "THO" && values.Length >= 7 && int.TryParse(values[4], out len))
                {
                    var touchHold = new TouchHold(chart, time) { TouchArea = GetTouchArea(values[5], key), IsFirework = values[6] == "1"};
                    note = touchHold;
                    if (values.Length >= 8) touchHold.TouchSize = values[7];
                    var duration = new Duration(note) { Bar = new Rational(len, RSL) };
                    note.Duration = duration;
                    if (!(values.Length == 8 || (values.Length == 7 && MA2Version == 102))) WarnParamsCount(lineNo, line, time);
                }
                else if (SlideTypeTool.IsSlide(cc) && values.Length >= 7 && int.TryParse(values[4], out var waitLen)
                         && int.TryParse(values[5], out len) && int.TryParse(values[6], out var endKey))
                {
                    endKey++;
                    Slide slide;
                    if (md == "CN")
                    { // 连接星星
                        if (waitLen > 0) Fail(Locale.MA2CNSlideHasWait, lineNo, line);
                        // 查找到前面的星星
                        var a = _slides.GetValueOrDefault((time, key));
                        if (a == null || a.Count == 0) Fail(Locale.MA2CNSlideNoPrevious, lineNo, line);
                        slide = a![0];
                        a.RemoveAt(0); // 移除出来，因为一会会按照新的结束时间加回去
                    }
                    else
                    { // 首根星星
                        slide = new Slide(chart, time) { Key = key }; // 先设置一个key，以供最后关联星星和星星头时一次性使用
                        note = slide; // 添加进谱面
                        var waitTime = new Duration(slide) { Bar = new Rational(waitLen, RSL) };
                        slide.WaitTime = waitTime;
                    }
                    
                    // 处理并添加当前segment
                    var segment = new SlideSegment(slide)
                    {
                        Type = Enum.Parse<SlideType>(cc), EndKey = endKey,
                        Duration = new Duration(slide) { Bar = new Rational(len, RSL) },
                    };
                    slide.segments.Add(segment);
                    
                    // 根据最新的segment结果，更新缓存里的值，以便万一有CN星星接在这后面的话，可以找到
                    _slides.Add((slide.EndTime, slide.EndKey), slide);
                    
                    if (values.Length != 7) WarnParamsCount(lineNo, line, time);
                }
                else
                {
                    if (bpmRead) Fail(Locale.InvalidMA2Sentence, lineNo, line); // 在主体部分的不认识语句，直接失败
                    else alerts.Add(new Alert(Warning, Locale.InvalidMA2SentenceWarning, lineNo, line.ToString())); // 在头部分的，只给一个警告
                    continue;
                }

                if (note == null) continue;
                switch (md)
                {
                    case "BR":
                        note.IsBreak = true;
                        break;
                    case "EX":
                        note.IsEx = true;
                        break;
                    case "BX":
                        note.IsBreak = true;
                        note.IsEx = true;
                        break;
                }
                note.Time = note.Time.CanonicalForm;
                chart.Notes.Add(note);
            }
            else
            {
                if (bpmRead) Fail(Locale.InvalidMA2Sentence, lineNo, line); // 在主体部分的不认识语句，直接失败
                else alerts.Add(new Alert(Warning, Locale.InvalidMA2SentenceWarning, lineNo, line.ToString())); // 在头部分的，只给一个警告
            }
        }

        if (chart.Notes.Count == 0)
        {
            alerts.Add(new Alert(Error, Locale.NoNotesInChart));
            throw new ConversionException(alerts);
        }
        if (!bpmRead)
        {
            alerts.Add(new Alert(Error, Locale.NoBPMInMA2));
            throw new ConversionException(alerts);
        }
        
        // 最后过一遍谱面：排序（以防万一MA2里指令乱序），和连接星星头
        chart.Sort();
        ConnectSlideHeads();
        
        return (chart, alerts);
    }

    private static string GetTouchArea(string series, int key)
    {
        var touchArea = series + key;
        return touchArea != "C1" ? touchArea : "C"; // C区没有C1，C1等于C中心
    }

    private void ConnectSlideHeads()
    {
        // 连接星星头
        Rational now = 0;
        Tap?[] taps = new Tap?[8];
        List<Slide>[] slideLists = [[], [], [], [], [], [], [], []]; // 8个
        HashSet<Tap> toRemove = [];

        void pair()
        {
            for (int i = 0; i < 8; i++)
            {
                if (taps[i] == null || slideLists[i].Count == 0) continue; // 当前时刻、当前键位不是星星和tap都有，所以不配对
                var pairedTap = taps[i]!;
                toRemove.Add(pairedTap);
                for (int j = 0; j < slideLists[i].Count; j++)
                {
                    if (j == 0) slideLists[i][j].OwnHead = pairedTap;
                    else slideLists[i][j].SharedHeadWith = slideLists[i][0];
                }
            }
        }
        
        foreach (var note in chart.Notes)
        {
            if (note.Time > now)
            {
                // 对之前时刻的执行配对
                pair();
                // 清空缓存
                now = note.Time;
                taps = new Tap?[8];
                foreach (var list in slideLists) list.Clear();
            } 
            else if (note.Time < now) throw Utils.Fail("时间倒流，说明Chart.Sort写错了");

            if (note is Tap tap and not Hold) taps[tap.Key - 1] ??= tap; // 只有在数组中原本为空的情况下才set。不然不set。这是因为万一有人写宴谱，在同一时刻同一键位上塞了大于一个tap（这显然是无理）怎么办（x）
            else if (note is Slide slide)
            {
                slide.Duration = slide.Duration; // 确保只有最后一段有时间
                slideLists[slide.Key - 1].Add(slide);
            }
        }
        pair(); // 最后一个时刻
        chart.Notes.RemoveAll(x => toRemove.Contains(x)); // 移除所有的toRemove
    }
}