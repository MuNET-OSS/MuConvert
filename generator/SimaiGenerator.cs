using System.Numerics;
using MuConvert.chart;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.generator;

class SimaiNote
{
    public Rational Time;
    public string Note;
    public int FalseEachIndex;
    public bool IsBpm;

    public SimaiNote(Rational time, string note, int falseEachIndex, bool isBpm = false)
    {
        Time = time;
        Note = note;
        FalseEachIndex = falseEachIndex;
        IsBpm = isBpm;
    }
};

public class SimaiGenerator : IGenerator
{
    /**
     * 这是一个Workaround的选项。
     * 形如[0.4567##8:1]的，组合使用等待时间秒数和持续时间分数的星星持续时长写法，尽管Simai官方文档中明确其为标准语法，但MajdataView中却无法显示出来。（会把这根星星，以及同时刻的所有其他音符（如有），全部给吞掉。）
     * 因此，这里提供了一个bool选项，默认为开启：
     * 当本选项开启时，不会生成形如[0.4567##8:1]的语句出来。对于非标准等待时间的星星，会一律写成[0.4567##0.125]这种前后都是绝对时间的格式。
     * 如果你不希望开启本选项，请new SimaiGenerator() { Workaround_ForceUseAbsDurationForSlidesWithNonStandardWaitTime = false } 即可。
     */
    public bool Workaround_ForceUseAbsDurationForSlidesWithNonStandardWaitTime = true;
    
    private readonly List<Alert> alerts = [];
    private string result = ""; // 不用StringBuilder是因为生成过程不可避免地需要对字符串做一些回溯的操作，需要倒着从字符串中查找字符。这样的场景下，StringBuilder并无性能优势，用string就够了。
#pragma warning disable CS8618
    private Chart chart;
#pragma warning restore CS8618
    
    private int bpmIdx = 0; // 当前遍历到了哪个bpm
    private List<SimaiNote> buf = [];
    
    private BigInteger curDiv = 0; // 当前的分音值状态
    private Rational writePtr = 0; // 当前所写到的位置
    // private bool isInAbsTimeDiv = false; // 为未来的需求预留，暂时用不上
    
    private Dictionary<Slide, SimaiNote> sharedHeadBuf = new(); // 以下两个用于控制星星头的缓存，确保同头星星可以直接被连接到正确的simai语句上

    // 切换分音
    private void ChangeDiv(BigInteger div)
    {
        // 向前找到前一个逗号或右括号，加在这里
        int i;
        int e = -1;
        for (i = result.Length - 1; i >= 0; i--)
        {
            var c = result[i];
            if (c is ',' or ')' or '{' or '\n')
            {
                if (c == '{') i--;
                break;
            }
            else if (c == '}') e = i;
        }
        if (e == -1) e = i;
        result = result[..(i + 1)] + $"{{{div}}}" + result[(e + 1)..];
        curDiv = div;
    }
    
    /**
     * 写入一定长度的逗号进入谱面。内部会自动切换分音等。
     * <param name="len">要写入的逗号（间隔）的长度</param>
     * <param name="forceAsIs">如果为true，则不会对输入的len进行约分、强制按len.Numerator和len.Denominator进行写入。</param>
     * <param name="autoNewLine">当进入新的一小节时，自动添加换行符</param>
     * <param name="breakOnNewLine">当添加换行符后，break，未完成的添加不再执行。仅当autoNewLine=true时才生效。</param>
     */
    private void WriteComma(Rational len, bool forceAsIs = false, bool autoNewLine = true, bool breakOnNewLine = false)
    {
        if (len == 0) return;
        if (!forceAsIs) len = len.CanonicalForm;
        var div = len.Denominator;
        var numer = len.Numerator;

        if (!forceAsIs)
        {
            var directCount = len * curDiv; // 本质是len / (1 / lastWritenBase)
            // 看看能不能用现有curDiv进行整除，如果能的话就别折腾别切换了
            if (curDiv > 0 && directCount.FractionPart == 0 && (directCount <= 4 || curDiv <= 16))
            {
                div = curDiv;
                numer = directCount.WholePart;
            }
        }

        if (div != curDiv) ChangeDiv(div);
        for (int i = 0; i < numer; i++)
        {
            var before = writePtr;
            result += ',';
            writePtr += new Rational(1, div);
            if (autoNewLine && writePtr.WholePart != before.WholePart)
            {
                result += "\r\n";
                if (breakOnNewLine) break;
            }
        }
    }

    private string DurationStr(Rational start, Duration duration, bool forceAbsTime = false)
    {
        var ib = duration.InvariantBar;
        // 当发生以下两种情况之一时，返回bar格式原值；否则返回秒
        // 1. duration全程bpm没有变化，且分母小于384
        // 2. 算出来的InvariantBar，分母小于等于16分音
        if (!forceAbsTime && (
                ib.Denominator <= 16 ||
                (!chart.BpmList.IsBpmChanged(start, start + ib) && ib.Denominator <= 384)
            ))
        {
            return $"[{ib.Denominator}:{ib.Numerator}]";
        }
        else
        { // 返回绝对时间
            return $"[#{(decimal)duration.Seconds:0.####}]";
        }
    }

    private string DurationStr(Note note) => DurationStr(note.Time, note.Duration);
    
    public (string, List<Alert>) Generate(Chart _chart)
    {
        if (chart != null) throw new Exception(Locale.InstanceMultipleUsage);
        chart = _chart;
        chart.Sort();

        // 遍历音符，生成SimaiNote中间表示（时间还是Rational、但音符内容已转为字符串），存入buf中
        int noteIdx = 0;
        while (noteIdx < chart.Notes.Count || bpmIdx < chart.BpmList.Count)
        { // 只要有音符没写入或bpm标记没写入，就继续
            // 如果noteIdx 不< chart.Notes.Count，还能走到这里的话一定是因为bpm还没写入完(bpmIdx < chart.BpmList.Count)。
            // 此时只要让time是一个特别大的数，确保下面的time >= chart.BpmList[bpmIdx].Time的逻辑能触发，就可以了。
            var note = noteIdx < chart.Notes.Count ? chart.Notes[noteIdx] : new Tap(null!, 999999999);
            var time = note.Time;
            
            // 先看是否引发bpm change，如果是的话，则本次循环只结算这个bpm change
            if (bpmIdx < chart.BpmList.Count && time >= chart.BpmList[bpmIdx].Time)
            {
                var bpmChange = chart.BpmList[bpmIdx];
                bpmIdx++;
                buf.Add(new SimaiNote(bpmChange.Time, $"({bpmChange.Bpm:0.#######})", 0, true));
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

                Rational rollingTime = slide.Time;
                foreach (var seg in slide.segments)
                {
                    res += seg.Type.ToSimai(seg.StartKey);
                    res += seg.EndKey;
                    
                    if (seg.Duration != null)
                    {
                        bool nonStdWaitTime = rollingTime == slide.Time && // 是带有时间标记的第一段
                                              slide.WaitTime.InvariantBar != new Rational(1, 4);
                        var durationStr = DurationStr(rollingTime, seg.Duration,
                            // 对非标准等待时间的星星，如果相关Workaround选项开启，则强制其使用绝对时间。
                            forceAbsTime: nonStdWaitTime && Workaround_ForceUseAbsDurationForSlidesWithNonStandardWaitTime);
                        if (nonStdWaitTime)
                        { // 非标准等待时间的星星，应该加上等待时间标记。simai仅支持绝对时间的等待时间标记。
                            durationStr = $"[{(decimal)slide.WaitTime.Seconds:0.####}##{durationStr[1..].TrimStart('#')}";
                        }
                        res += durationStr;
                        rollingTime += seg.Duration.Bar;
                    }
                    else if (rollingTime != slide.Time)
                    { // 说明此前已经有某段出现过时间标记了，即这根星星是分段时间的写法。但我自己现在却没有时间，说明是非法的写法
                        alerts.Add(new Alert(Error, Locale.InvalidSlide, (chart, time), null, res));
                        throw new ConversionException(alerts);
                    }
                }
                res += slide.Modifiers; // 根据simai文档，slide的全局修饰符（绝赞星星）加在最后一段星星的时间标记的后面

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
                    var simaiNote = new SimaiNote(time, res, slide.FalseEachIdx);
                    buf.Add(simaiNote);
                    res = ""; // 我自己加进simaiNote里去，循环外面的公共逻辑就不要加了
                    sharedHeadBuf[slide] = simaiNote;
                }
            }
            else throw Utils.Fail("SimaiGenerator遇到了未知的Note对象");

            if (!string.IsNullOrEmpty(res)) buf.Add(new SimaiNote(time, res, note.FalseEachIdx));
            noteIdx++;
        }
        
        // 基于buf中的内容，写入result生成字符串
        BigInteger baseDiv = 1; // 目标的div数。生成逗号时，会尽量使结果接近这个目标div数。
        BigInteger baseDivBar = -1; // 上述baseDiv对应的小节编号。我们的策略是对每个小节重算baseDiv，因此需要记录这个信息。
        for (int i = 0; i < buf.Count;i++)
        {
            var note = buf[i];
            
            # region 处理多押
            // 非常简单，发现是多押，直接append即可，下面的逻辑全不需要管
            if (i > 0 && note.Time == buf[i-1].Time && !buf[i-1].IsBpm)
            {
                // 看FalseEachIndex是否有增大，决定用"/"还是"`"连接
                var isFalseEach = note.FalseEachIndex > buf[i - 1].FalseEachIndex;
                result += (isFalseEach ? '`' : '/') + note.Note;
                continue; // 退出循环，下面的逻辑不走了
            }
            # endregion
            
            # region 仅在每小节的第一个音符时才调用到的逻辑
            bool? shouldFillLastBarFirst = null;
            if (note.Time.WholePart > baseDivBar)
            {
                baseDivBar = note.Time.WholePart;
                (baseDiv, shouldFillLastBarFirst) = CalculateBaseDiv(i);
            }
            if (shouldFillLastBarFirst != null)
            { 
                // 说明是刚刚发生CalculateBaseDiv(i)后的小节首音符。此时：
                // 1. 需要按照shouldFillLastBarFirst中的说明进行处理
                // 2. 可能需要添加完整的空白小节进去。
                if (shouldFillLastBarFirst.Value)
                {
                    var toFill = (baseDivBar - writePtr).FractionPart;
                    WriteComma(toFill);
                }
                else if (writePtr.FractionPart != 0)
                {
                    WriteComma(note.Time - writePtr, breakOnNewLine: true);
                }
                // 把多余的整小节添加进去
                var wholeBarToFill = (note.Time - writePtr).WholePart;
                for (var j = 0; j < wholeBarToFill; j++)
                {
                    WriteComma(1, true, j == wholeBarToFill - 1); // 当出现连续多个空小节时，只有最后一个需要在结尾加换行符
                }
            }
            #endregion
            
            // 添加音符之前的空白
            var blank = note.Time - writePtr;
            WriteBlank(blank, baseDiv);
            result += note.Note;
            // if (note.IsBpm) { ChangeDiv(...) } // 为未来isInAbsTimeDiv的需求预留
        }
        result += ",\r\nE"; // 结束标记
        
        return (result, alerts);
    }

    private void WriteBlank(Rational blank, BigInteger baseDiv)
    { // 抽成一个单独的函数，方便递归调用
        blank = blank.CanonicalForm;
        var t = new Rational(baseDiv, blank.Denominator);
        if (t >= 1 && t.FractionPart == 0)
        { // 当前要添加的时间的分音小于baseDiv的情况，强制按照baseDiv进行添加。
            Rational value = new(blank.Numerator * t.WholePart, baseDiv);
            WriteComma(value, true);
            return;
        }

        var curAim = Utils.Max(blank.Denominator / 4, baseDiv);
        var wholeAims = new Rational((blank * curAim).WholePart, curAim);
        var remain = blank - wholeAims;
        WriteComma(remain);
        WriteBlank(wholeAims, baseDiv);
    }

    private (BigInteger, bool) CalculateBaseDiv(int noteIdx)
    {
        BigInteger bar = buf[noteIdx].Time.WholePart;
        List<Rational> gaps = [buf[noteIdx].Time - bar]; // 每个音符前面的间隔时间
        for (int i = noteIdx+1; i < buf.Count; i++)
        {
            if (buf[i].Time.WholePart > bar) break;
            gaps.Add(buf[i].Time - buf[i-1].Time);
        }
        
        // 计算两种最小公倍数LCM：
        // 第一种，对所有的间隔计算分母的LCM，作为“此种方式是否更优的标准”
        // 第二种，仅对TH_DIRECT以下的分音才纳入考虑的计算LCM，作为base的基础
        var (lcm_1, lcm_2) = LCM(gaps);
        var resultDiv = lcm_2;
        var shouldFillLastBarFirst = true;
        
        // 上述假设的是在正式开始应该先把上一小节补完的情况。
        // 下面，假设不需要把上一小节补完，重算一次。
        var remainTime = bar - writePtr;
        if (remainTime > 0)
        {
            gaps[0] += remainTime;
            var (lcm_n_1, lcm_n_2) = LCM(gaps);
            if (lcm_n_1 < lcm_1)
            { // 说明刚刚的这种计算方式更优
                resultDiv = lcm_n_2;
                shouldFillLastBarFirst = false;
            }
        }
        
        return (resultDiv, shouldFillLastBarFirst);
    }

    private const int TH_DIRECT = 16; // base分音的最大值，也是第二种LCM计算时过滤的阈值。超过这个值，不能成为base分音
    private const int DIRECT_MINVAL = 4; // 当第一种LCM大于第二种时，第二种不可以小于这个值。
    
    private (BigInteger, BigInteger) LCM(List<Rational> gaps)
    {
        var data = gaps.Where(x => x > 0).Select(x => x.CanonicalForm.Denominator).ToList();
        var lcm_1 = data.Count > 0 ? Utils.LCM(data) : 1;
        data = data.Where(x => x <= TH_DIRECT).ToList();
        var lcm_2 = data.Count > 0 ? Utils.LCM(data) : 1;
        if (lcm_1 > TH_DIRECT && lcm_2 < DIRECT_MINVAL) lcm_2 = DIRECT_MINVAL; // 当lcm_1 > TH_DIRECT（也就是大于lcm_2）时，lcm_2不得小于DIRECT_MINVAL；反之，如果lcm_1 <= TH_DIRECT，则说明第二次过滤得到的data和第一次必定是一样的，则lcm_1必定==lcm_2。
        return (lcm_1, lcm_2);
    }
}