using System.Numerics;
using System.Text;
using MuConvert.chart;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.generator;

class SimaiNote
{
    public Rational Time;
    public string Note;
    public bool IsBpm;

    public SimaiNote(Rational time, string note, bool isBpm = false)
    {
        Time = time;
        Note = note;
        IsBpm = isBpm;
    }
};

public class SimaiGenerator : IGenerator
{
    private readonly List<Alert> alerts = [];
    private StringBuilder result = new StringBuilder();
#pragma warning disable CS8618
    private Chart chart;
#pragma warning restore CS8618
    
    private List<SimaiNote> buf = [];
    private int bpmIdx = 0; // 当前遍历到了哪个bpm
    
    private BigInteger bufBarWholepart = 0; // 以下两个用于控制分小节写入
    private bool lastWriteEmpty = false;
    private Dictionary<Slide, SimaiNote> sharedHeadBuf = new(); // 以下两个用于控制星星头的缓存，确保同头星星可以直接被连接到正确的simai语句上
    private Rational sharedHeadBufTime = 0;

    private void flush()
    {
        throw new NotImplementedException();
    }

    private string DurationStr(Note note)
    {
        var ib = note.Duration.InvariantBar;
        // 当发生以下两种情况之一时，返回bar格式原值；否则返回秒
        // 1. duration全程bpm没有变化
        // 2. 算出来的InvariantBar，分母小于等于16分音
        if (ib.Denominator <= 16 || !chart.BpmList.IsBpmChanged(note.Time, note.Time + ib))
        {
            return $"[{ib.Denominator}:{ib.Numerator}]";
        }
        else
        { // 返回绝对时间
            return $"[#{(decimal)note.Duration.Seconds:0.###}]";
        }
    }
    
    public (string, List<Alert>) Generate(Chart _chart)
    {
        if (chart != null) throw new Exception(Locale.InstanceMultipleUsage);
        chart = _chart;

        int noteIdx = 0;
        while (noteIdx < chart.Notes.Count)
        {
            var note = chart.Notes[noteIdx];
            var time = note.Time;
            
            // 先看是否引发bpm change，如果是的话，则本次循环只结算这个bpm change
            BPM? bpmChange = null;
            if (bpmIdx < chart.BpmList.Count && time >= chart.BpmList[bpmIdx].Time)
            {
                bpmChange = chart.BpmList[bpmIdx];
                time = chart.BpmList[bpmIdx].Time;
            }
            
            // 基于时间的缓存清理
            while (time.WholePart > bufBarWholepart)
            { // 如果进入了新的小节，则写入上一小节缓存下来的数据
                flush();
                bufBarWholepart++;
            }
            if (time != sharedHeadBufTime)
            { // 清空sharedHeadBuf
                sharedHeadBuf.Clear();
                sharedHeadBufTime = time;
            }
            
            if (bpmChange != null)
            { // 本次循环只结算这个bpm change
                buf.Add(new SimaiNote(bpmChange.Time, $"({bpmChange.Bpm})", true));
                continue;
            }

            string res;
            if (note is Hold hold)
            {
                res = $"{hold.Key}h{hold.Modifiers}{DurationStr(hold)}";
            }
            else if (note is Tap tap)
            {
                var starModifier = tap is Star ? "$" : "";
                res = $"{tap.Key}{starModifier}{tap.Modifiers}";
            }
            else if (note is TouchHold th)
            {
                res = $"{th.TouchArea}h{th.Modifiers}{DurationStr(th)}";
            }
            else if (note is Touch touch)
            {
                res = $"{touch.TouchArea}{touch.Modifiers}";
            }
            else if (note is Slide slide)
            {
                // 处理头
                if (slide.SharedHeadWith != null) res = "*";
                else if (slide.OwnHead != null)
                {
                    var starModifier = slide.OwnHead is not Star ? "@" : "";
                    res = $"{slide.Key}{starModifier}{slide.OwnHead.Modifiers}";
                }
                else res = $"{slide.Key}?";

                bool durationOccured = false;
                foreach (var seg in slide.segments)
                {
                    res += seg.Type.ToSimai(seg.StartKey);
                    res += seg.EndKey;
                    
                    if (seg.Duration != null)
                    {
                        durationOccured = true;
                    }
                    else if (durationOccured)
                    { // 如果此前有某段出现过时间，说明是分段时间的写法。但我自己却没有时间，说明是非法的写法
                        alerts.Add(new Alert(Error, Locale.InvalidSlide, (chart, time), null, res));
                        throw new ConversionException(alerts);
                    }
                }

                if (slide.SharedHeadWith != null)
                {
                    if (!sharedHeadBuf.TryGetValue(slide.SharedHeadWithRoot, out var src))
                    { // 没找到前一段星星对应的字符串
                        alerts.Add(new Alert(Error, Locale.MA2CNSlideNoPrevious, (chart, time), null, res));
                        throw new ConversionException(alerts);
                    }
                    Utils.Assert(src.Time == time);
                    src.Note += res;
                    res = "";
                }
                else
                {
                    var simaiNote = new SimaiNote(time, res);
                    buf.Add(simaiNote);
                    res = ""; // 我自己加进simaiNote里去，循环外面的公共逻辑就不要加了
                    sharedHeadBuf[slide] = simaiNote;
                }
            }
            else throw Utils.Fail("SimaiGenerator遇到了未知的Note对象");

            if (!string.IsNullOrEmpty(res)) buf.Add(new SimaiNote(time, res));
            noteIdx++;
        }

        return ("", alerts);
    }
}