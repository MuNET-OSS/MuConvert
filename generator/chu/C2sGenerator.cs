using System.Text;
using MuConvert.generator;
using MuConvert.utils;
using static MuConvert.utils.ChuUtils;

namespace MuConvert.chu;

public class C2sGenerator : IGenerator<ChuChart>
{
    private const int RSL = 384;

    private HashSet<string> slaLines = [];
    
    public (string, List<Alert>) Generate(ChuChart chart)
    {
        var alerts = new List<Alert>();
        var text = Serialize(chart, alerts);
        return (text, alerts);
    }

    private string Serialize(ChuChart chart, List<Alert> alerts)
    {
        chart.Sort();
        
        int.TryParse(chart.MusicId, out var musicId);
        var sb = new StringBuilder();
        sb.AppendLine($"VERSION\t1.14.00\t1.14.00");
        sb.AppendLine($"MUSIC\t{musicId}");
        sb.AppendLine("SEQUENCEID\t0");
        sb.AppendLine($"DIFFICULT\t{chart.Difficulty:D2}");
        sb.AppendLine($"LEVEL\t{chart.Level:F1}");
        sb.AppendLine($"CREATOR\t{chart.Designer}");
        var bpm_def = chart.BpmList.BPM_DEF();
        sb.AppendLine(FormattableString.Invariant($"BPM_DEF\t{bpm_def.Item1:F3}\t{bpm_def.Item2:F3}\t{bpm_def.Item3:F3}\t{bpm_def.Item4:F3}"));
        sb.AppendLine("MET_DEF\t4\t4");
        sb.AppendLine($"RESOLUTION\t{RSL}");
        sb.AppendLine($"CLK_DEF\t{RSL}");
        sb.AppendLine("PROGJUDGE_BPM\t240.000");
        sb.AppendLine("PROGJUDGE_AER\t  0.999");
        sb.AppendLine("TUTORIAL\t0");
        sb.AppendLine($"GENERATED_BY\tMuConvert v{Utils.AppVersion}");
        sb.AppendLine();

        foreach (var b in chart.BpmList)
        {
            var (m, o) = Utils.BarAndTick(b.Time, RSL);
            sb.AppendLine(FormattableString.Invariant($"BPM\t{m}\t{o}\t{b.Bpm:0.000}"));
        }

        foreach (var met in chart.MetList)
        {
            var (m, o) = Utils.BarAndTick(met.Time, RSL);
            sb.AppendLine($"MET\t{m}\t{o}\t{met.Denominator}\t{met.Numerator}");
        }

        // 生成SLP
        foreach (var (sfl, groupId) in chart.SpeedGroups
                     .SelectMany(x=>x.Value, (x, sfl) => (sfl, x.Key))
                     .OrderBy(x=>(x.sfl.Time, x.Key)))
        {
            var (m, o) = Utils.BarAndTick(sfl.Time, RSL);
            var durTicks = Utils.Tick(sfl.Duration, RSL);
            sb.AppendLine(FormattableString.Invariant($"SLP\t{m}\t{o}\t{durTicks}\t{sfl.Multiplier:0.000000}\t{groupId}"));
        }
        
        sb.AppendLine();

        slaLines = [];
        List<string> resultLines = [];
        foreach (var n in chart.Notes)
        {
            var lines = FormatNote(n, alerts);
            resultLines.AddRange(lines);
        }

        foreach (var line in resultLines.Concat(slaLines).OrderBy(_lineOrder))
        {
            sb.AppendLine(line);
        }
        sb.AppendLine();
        return sb.ToString();

        (int, int) _lineOrder(string line)
        {
            var s = line.Split('\t');
            return (int.Parse(s[1]), int.Parse(s[2]));
        }
    }

    private List<string> FormatNote(ChuNote n, List<Alert> alerts)
    {
        var (m, o) = Utils.BarAndTick(n.Time, RSL);
        List<List<string>> results = [];
        
        if (n.Type is ChuNoteType.Tap or ChuNoteType.Flick or ChuNoteType.Mine)
        {
            var name = n.Type switch
            {
                ChuNoteType.Tap => n.IsAir ? n.AirDirection.ToString()
                    : (n.IsEx ? "CHR" : "TAP"),
                ChuNoteType.Flick => "FLK",
                ChuNoteType.Mine => "MNE",
                _ => throw Utils.Fail(),
            };
            List<string> r = [name, m.ToString(), o.ToString(), n.Cell.ToString(), n.Width.ToString()];

            if (n.Type == ChuNoteType.Tap && n.IsEx) r.Add(ExDirections_ToUgc[n.Ex!.Value]); // CHR
            else if (n.Type == ChuNoteType.Flick) r.Add(n.Ex == ExDirection.RS ? "R" : "L"); // FLK
            else if (n.IsAir)
            { // AIR
                var targetStr = AsC2sPreviousStr(n.TargetNote);
                Utils.Assert(targetStr != null, "MuConvert内部错误：Air音符的TargetNote出现了不合法的类型！");
                r.AddRange(targetStr!, AirColor_ToUgc(n));
            }
            
            results.Add(r);
            if (n.SpeedGroup != 0) // 分音符变速组 SLA 的处理
                results.Add(["SLA", ..r[1..5], "1", n.SpeedGroup.ToString()]);
        }
        else
        {
            var name = (n.Type, n.IsAir) switch
            {
                (ChuNoteType.Hold, true) => n.IsEx ? "AHX" : "AHD",
                (ChuNoteType.Hold, false) => n.IsEx ? "HXD" : "HLD",
                (ChuNoteType.Slide, true) => "ASD",
                (ChuNoteType.Slide, false) => n.IsEx ? "SXD" : "SLD",
                (ChuNoteType.Crush, true) => "ALD",
                _ => throw Utils.Fail(),
            };
            
            var start = (n.Time, n.Cell, n.Width, n.Height);
            foreach (var seg in n.Segments)
            {
                // 必备的：name bar tick cell width
                var (sB, sT) = Utils.BarAndTick(start.Time, RSL);
                List<string> r = [name, sB.ToString(), sT.ToString(), start.Cell.ToString(), start.Width.ToString()];
                
                // 前驱targetNote 或 crushInterval
                if (NeedsTargetNote(n))
                {
                    var targetStr = AsC2sPreviousStr(n.TargetNote);
                    Utils.Assert(targetStr != null, "MuConvert内部错误：Air音符的TargetNote出现了不合法的类型！");
                    r.Add(targetStr!);
                }
                else if (n.Type == ChuNoteType.Crush)
                    r.Add(Utils.Tick(n.CrushInterval ?? 100, RSL).ToString());

                // Air且不是Air-Hold的情况，这里要加上height
                if (n.IsAir && n.Type != ChuNoteType.Hold)
                    r.Add($"{n.Height:F1}");
                
                // 持续时间
                // 为了保持两段之间紧密相接，durTicks必须通过end和start的tick直接作差得到，不能把seg.Length直接转tick，
                // 不然可能会由于舍入误差，导致差1tick接不上等情况。
                var endTime = start.Time + seg.Length;
                var durTicks = Utils.Tick(endTime, RSL) - Utils.Tick(start.Time, RSL);
                r.Add(durTicks.ToString());

                // 结束的 cell width [height]
                if (n.Type is ChuNoteType.Slide or ChuNoteType.Crush)
                {
                    r.AddRange([seg.EndCell.ToString(), seg.EndWidth.ToString()]);
                    if (n.IsAir) r.Add($"{seg.EndHeight:F1}");
                }

                // 颜色
                if (n.IsAir) r.Add(n.Color.ToString());
                else if (n.Type == ChuNoteType.Slide) r.Add("SLD"); // C2S 1.13版本以上，Slide在最后，还需要加一个”SLD“后缀
                
                // Ex音符的话，要加上Ex方向
                if (n.IsEx) r.Add(ExDirections_ToUgc[n.Ex!.Value]);
                
                results.Add(r);
                start = (endTime, seg.EndCell, seg.EndWidth, seg.EndHeight);
                if (n.SpeedGroup != 0) // 分音符变速组 SLA 的处理
                    results.Add(["SLA", ..r[1..5], "1", n.SpeedGroup.ToString()]);
            }
            if (n.SpeedGroup != 0)
            { // 分音符变速组 SLA 的处理：参照PenguinTools的实现，要把最后一段的结束点，也加到SLA上
                var (sB, sT) = Utils.BarAndTick(start.Time, RSL); // 这里的start.Time，其实是最后一段的endTime
                List<string> sParts = [sB.ToString(), sT.ToString(), start.Cell.ToString(), start.Width.ToString()]; // start.Cell/Width，实际上也是最后一段的endCell/endWidth
                results.Add(["SLA", ..sParts, "1", n.SpeedGroup.ToString()]);
            }
        }
        
        return results.Select(x=>string.Join('\t', x)).ToList();
    }
}
