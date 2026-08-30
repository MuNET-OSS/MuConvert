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
            var line = FormatNote(n, alerts);
            if (line != null) resultLines.Add(line);
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

    private static string AirColorTag(ChuNote n, List<Alert> alerts)
    {
        if (C2sAllowedColors.Contains(n.Tag)) return n.Tag;
        else
        {
            if (n.Tag != "") alerts.Add(new Alert(Alert.LEVEL.Warning, string.Format(Locale.C2SUnsupportedAirColor, "C2S Generator", n.Type, n.Tag), n.Time));
            return "DEF";
        }
    }
    private static string AirCrushColorTag(ChuNote n, List<Alert> alerts)
    {
        if (C2sAllowedCrushColors.Contains(n.Tag)) return n.Tag;
        else
        {
            if (n.Tag != "") alerts.Add(new Alert(Alert.LEVEL.Warning, string.Format(Locale.C2SUnsupportedAirColor, "C2S Generator", n.Type, n.Tag), n.Time));
            return "DEF";
        }
    }

    private static string FLKTag(ChuNote n) => n.Tag is "L" or "R" ? n.Tag : "L";
    
    private string? FormatNote(ChuNote n, List<Alert> alerts)
    {
        var (m, o) = Utils.BarAndTick(n.Time, RSL);
        var durTicks = Utils.Tick(n.Duration, RSL);
        if (IsChainContinueSegments(n))
        { // 特殊地，对于slide的后续段：为了保证能接上，必须保证start tick接在前一个音符的endTime的后面，duration也采用end-start的方式计算durTicks。否则可能会，因为舍入的误差，造成没有办法接起来。
            (m, o) = Utils.BarAndTick(n.Previous!.EndTime, RSL);
            durTicks = Utils.Tick(n.EndTime, RSL) - Utils.Tick(n.Previous!.EndTime, RSL);
        }
        var result = n.Type switch
        {
            "TAP" or "CHR" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}",
            "HLD" or "HXD" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{durTicks}",
            "SLD" or "SLC" or "SXD" or "SXC" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}\tSLD",
            "FLK" => $"FLK\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{FLKTag(n)}",
            "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL" =>
                    $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{AirColorTag(n, alerts)}",
            "AHD" or "AHX" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{durTicks}\t{AirColorTag(n, alerts)}",
            "ASD" or "ASC" => FormatAsdAsc(n, m, o, durTicks, alerts),
            "ALD" => FormatAld(n, m, o, durTicks, alerts),
            "MNE" => $"MNE\t{m}\t{o}\t{n.Cell}\t{n.Width}",
            _ => alert(),
        };
        if (result == null) return null;
        if (n.Type is "CHR" or "HXD" or "SXD" or "SXC") result += $"\t{n.Tag}";

        #region 分音符变速组 SLA 的处理
        if (n.SpeedGroup != 0)
        {
            slaLines.Add($"SLA\t{m}\t{o}\t{n.Cell}\t{n.Width}\t1\t{n.SpeedGroup}");
            if (durTicks > 0)
            {
                var (endM, endO) = Utils.BarAndTick(n.EndTime, RSL);
                var (endC, endW) = IsSlide(n) || IsAirSlide(n) || IsAirCrush(n) ? (n.EndCell, n.EndWidth) : (n.Cell, n.Width);
                slaLines.Add($"SLA\t{endM}\t{endO}\t{endC}\t{endW}\t1\t{n.SpeedGroup}");
            }
        }
        #endregion
        
        return result;

        string? alert()
        {
            alerts.Add(new Alert(Alert.LEVEL.Warning, Locale.C2SUnknownNoteType, n.Time));
            return null;
        }
    }

    private static string FormatAsdAsc(ChuNote n, int m, int o, int durTicks, List<Alert> alerts)
    {
        return FormattableString.Invariant($"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{n.Height:F1}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}\t{n.EndHeight:F1}\t{AirColorTag(n, alerts)}");
    }

    private static string FormatAld(ChuNote n, int m, int o, int durTicks, List<Alert> alerts)
    {
        return FormattableString.Invariant($"ALD\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{Utils.Tick(n.CrushInterval, RSL)}\t{n.Height:F1}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}\t{n.EndHeight:F1}\t{AirCrushColorTag(n, alerts)}");
    }
}
