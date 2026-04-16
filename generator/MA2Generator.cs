using System.Text;
using MuConvert.chart;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.generator;

record MA2Line(string Name, int Bar, int Tick, int Key, string Extra = "");

public class MA2Generator : IGenerator
{
    public MA2Generator(bool isUtage = false)
    {
        IsUtage = isUtage;
    }

    // 除非你知道你在做什么，不然以下两个变量请勿修改！
    public bool IsUtage;
    public int MA2Version = 105;
    public int RSL = 384;
    
    private List<MA2Line> lines = [];
    private readonly List<Alert> alerts = [];
    
    private string headTemplate = @"VERSION	0.00.00	{0}
FES_MODE	{1}
BPM_DEF	{2:F3}	{3:F3}	{4:F3}	{5:F3}
MET_DEF	4	4
RESOLUTION	{6}
CLK_DEF	{7}
COMPATIBLE_CODE	MA2
GENERATED_BY	MuConvert v{8}

";

    private Rational __1_384 = new(1, 384);
    
    private (decimal, decimal, decimal, decimal) bpmStats(Chart chart)
    {
        var bpms = chart.BpmList.Select(x => x.Bpm).ToList();
        var max = bpms.Max();
        var min = bpms.Min();
        var modes = bpms.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key; // 众数
        return (chart.BpmList.First().Bpm, modes, max, min);
    }

    /**
     * 把Rational的时间近似到RESOLUTION允许的最接近tick上
     */
    private (int, int) BT(Rational r, int offset = 0)
    {
        if (offset != 0) r += new Rational(offset, RSL);
        return ((int)r.WholePart, (int)Math.Round((double)(r.FractionPart * RSL)));
    }

    // 持续时间/等待时间，使用"总tick数"（可超过1小节），不是小节内tick
    private int T(Rational r, int offset = 0)
    {
        var result = (int)Math.Round((double)(r* RSL));
        if (offset != 0) result = Math.Max(result + offset, result > 0 ? 1 : 0);
        return result;
    }

    private void AddTap(Tap tap, int bar, int tick)
    {
        var prefix = "NM";
        if (tap.IsBreak && tap.IsEx) prefix = "BX";
        else if (tap.IsBreak) prefix = "BR";
        else if (tap.IsEx) prefix = "EX";
        var name = tap is Star ? "STR" : "TAP";
                
        string extra = "";
        if (tap is Hold hold)
        {
            name = "HLD";
            extra = T(hold.Duration.Bar, -hold.FalseEachIdx).ToString();
        } 
        lines.Add(new MA2Line(prefix + name, bar, tick, tap.Key - 1, extra));
    }
    
    public (string, List<Alert>) Generate(Chart chart)
    {
        if (lines.Count != 0) throw new Exception(Locale.InstanceMultipleUsage);
        chart.Sort();
        StringBuilder result = new StringBuilder();
        
        // 文件头
        var bpmStatistics = bpmStats(chart);
        string head = string.Format(headTemplate, 
            $"{MA2Version / 100}.{MA2Version % 100:D2}.00", IsUtage?1:0, 
            bpmStatistics.Item1, bpmStatistics.Item2,  bpmStatistics.Item3, bpmStatistics.Item4,
            RSL, RSL/4 * chart.ClockCount, Utils.AppVersion);
        result.Append(head);
        
        // bpm段
        foreach (var bpm in chart.BpmList)
        {
            var (bar, tick) = BT(bpm.Time);
            result.AppendLine($"BPM\t{bar}\t{tick}\t{bpm.Bpm:F3}");
        }
        result.AppendLine($"MET\t0\t0\t4\t{chart.ClockCount}");
        result.AppendLine();
        
        // 主体：音符段
        // 由于fes星星涉及一个重排序的问题，同时也为了后面统计方便，先把note放进lines数组中最后一块写入，而不是直接写入文件
        for (int noteIdx = 0; noteIdx < chart.Notes.Count; noteIdx++)
        {
            var note = chart.Notes[noteIdx];
            if (noteIdx > 0)
            {
                var distToPrev = note.Time - chart.Notes[noteIdx - 1].Time;
                if (distToPrev > 0 && distToPrev < __1_384)
                {
                    alerts.Add(new Alert(Warning, Locale.NoteTooNear, (chart, note.Time)));
                }
            }
            
            var (bar, tick) = BT(note.Time, note.FalseEachIdx);
            if (note is Tap tap)
            {
                AddTap(tap, bar, tick);
            }
            else if (note is Touch touch)
            {
                const string prefix = "NM"; // touch目前只有normal的
                var name = "TTP";
                List<string> extras = [];
                if (note is TouchHold th)
                {
                    name = "THO";
                    extras.Add(T(th.Duration.Bar, -th.FalseEachIdx).ToString());
                }

                var area = touch.TouchArea[0];
                var key = area != 'C' ? touch.Key - 1 : 0; // 目前，官机还不支持C1和C2分别写touch
                extras.Add(area.ToString());
                extras.Add(touch.IsFirework ? "1" : "0");
                extras.Add(touch.TouchSize);
                
                lines.Add(new MA2Line(prefix + name, bar, tick, key, string.Join("\t", extras)));
            }
            else if (note is Slide slide)
            {
                if (slide.OwnHead != null) AddTap(slide.OwnHead, bar, tick);
                
                // 首先很重要的一点是，详见 https://github.com/Neskol/MaiLib/issues/46#issuecomment-3301893924 ，
                // 官机现在对于多段星星，是会无视掉每一段分别指定的时长，把总时长加和然后全程匀速处理的。
                // 至少在我上述测试的版本是这样；但为了防止万一我测试错了、或者将来相关的行为改变，这里还是尊重chart原始记法、分两类处理。
                var totalLen = T(slide.Duration.Bar);
                
                # region 把时长平均分配给所有没有显式写出时长的段
                List<int?> segmentValue = [];
                var unassignedValue = totalLen;
                var unassignedCount = 0;
                for (int i = 0; i < slide.segments.Count - 1; i++)
                {
                    var seg = slide.segments[i];
                    if (seg.Duration != null)
                    {
                        var t = T(seg.Duration.Bar);
                        segmentValue.Add(t);
                        unassignedValue -= t;
                    }
                    else
                    {
                        segmentValue.Add(null);
                        unassignedCount++;
                    }
                }
                unassignedCount++; // 对应于最后一段
                var toAssignValue = unassignedValue / unassignedCount; // 未分配的时间分配给所有未分配段，每段分配到的量
                # endregion
                
                int segIdx;
                for (segIdx = 0; segIdx < slide.segments.Count; segIdx++)
                {
                    var seg = slide.segments[segIdx];
                    var len = segIdx == slide.segments.Count - 1 ? 
                        totalLen : // 对于最后一段，剩的时间全给它。以保证总长是正确的。
                        segmentValue[segIdx] ?? toAssignValue; // 除此之外，则是优先使用显式分配的时间、没有则使用平均时间
                    totalLen -= len;
                    int waitTime = 0;
                    
                    var prefix = "NM";
                    if (segIdx == 0)
                    {
                        if (slide.IsBreak) prefix = "BR";
                        waitTime = T(slide.WaitTime.Bar, -slide.FalseEachIdx);
                    }
                    else prefix = "CN";

                    var name = seg.Type.ToString();
                    
                    lines.Add(new MA2Line(prefix + name, bar, tick, seg.StartKey - 1,
                        string.Join("\t", [waitTime, len, seg.EndKey - 1])));
                    tick += waitTime + len;
                    while (tick >= RSL) { tick -= RSL; bar++; }
                }
            }
        }

        lines = lines.OrderBy(x => x.Bar * RSL + x.Tick).ToList();
        foreach (var l in lines)
        {
            var extra = !string.IsNullOrEmpty(l.Extra) ? "\t" + l.Extra : "";
            result.AppendLine($"{l.Name}\t{l.Bar}\t{l.Tick}\t{l.Key}{extra}");
        }
        result.AppendLine();
        
        // 统计段
        var stats = chart.Statistics;
        foreach (var (k, v) in statsNameConversion())
        {
            result.AppendLine($"T_REC_{k}\t{stats.Data.GetValueOrDefault(v)}");
        }
        var totalNum = stats.Total;
        result.AppendLine($"T_REC_ALL\t{totalNum}");

        var statsScoring = stats.ByScoring;
        result.AppendLine($"T_NUM_TAP\t{statsScoring["TAP"] + statsScoring["TOUCH"]}");
        result.AppendLine($"T_NUM_BRK\t{statsScoring["BREAK"]}");
        result.AppendLine($"T_NUM_HLD\t{statsScoring["HOLD"]}");
        result.AppendLine($"T_NUM_SLD\t{statsScoring["SLIDE"]}");
        result.AppendLine($"T_NUM_ALL\t{totalNum}");

        var statsNoteType = stats.ByNoteType;
        var stats_judge = new Dictionary<string, int>
        {
            ["TAP"] = statsNoteType["TAP"] + statsNoteType["STR"] + statsNoteType["TTP"],
            ["HLD"] = stats.T_JUDGE_HLD,
            ["SLD"] = statsNoteType["SLD"],
        };
        foreach (var (k, v) in stats_judge)
        {
            result.AppendLine($"T_JUDGE_{k}\t{v}");
        }
        result.AppendLine($"T_JUDGE_ALL\t{stats_judge.Sum(x=>x.Value)}");
        
        result.AppendLine($"TTM_EACHPAIRS\t{stats.TTM_EACHPAIRS}");
        
        result.AppendLine($"TTM_SCR_TAP\t{(statsScoring["TAP"] + statsScoring["TOUCH"]) * 500}");
        result.AppendLine($"TTM_SCR_BRK\t{statsScoring["BREAK"] * 2600}");
        result.AppendLine($"TTM_SCR_HLD\t{statsScoring["HOLD"] * 1000}");
        result.AppendLine($"TTM_SCR_SLD\t{statsScoring["SLIDE"] * 1500}");
        var theoryScore = stats.OldScore;
        result.AppendLine($"TTM_SCR_ALL\t{theoryScore}");
        
        var score_sss = stats.WeightedNoteCount * 500; // 旧框扣除额外分
        result.AppendLine($"TTM_SCR_S\t{Math.Ceiling(score_sss * 0.97 / 50) * 50}");
        result.AppendLine($"TTM_SCR_SS\t{score_sss}");
        result.AppendLine($"TTM_RAT_ACV\t{(long)theoryScore * 10000 / score_sss }"); // 用long避免溢出
        
        return (result.ToString(), alerts);
    }

    private Dictionary<string, string> statsNameConversion() => new()
    {
        ["TAP"] = "NMTAP", ["BRK"] = "BRTAP", ["XTP"] = "EXTAP", ["BXX"] = "BXTAP",
        ["HLD"] = "NMHLD", ["XHO"] = "EXHLD", ["BHO"] = "BRHLD", ["BXH"] = "BXHLD",
        ["STR"] = "NMSTR", ["BST"] = "BRSTR", ["XST"] = "EXSTR", ["XBS"] = "BXSTR",
        ["TTP"] = "NMTTP", ["THO"] = "NMTHO", 
        ["SLD"] = "NMSLD", ["BSL"] = "BRSLD",
    };
}