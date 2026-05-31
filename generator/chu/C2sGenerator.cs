using System.Text;
using MuConvert.generator;
using MuConvert.utils;
using static MuConvert.utils.ChuUtils;

namespace MuConvert.chu;

public class C2sGenerator : IGenerator<ChuChart>
{
    private const int RSL = 384;
    
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
        sb.AppendLine($"VERSION\t1.08.00\t1.08.00");
        sb.AppendLine($"MUSIC\t{musicId}");
        sb.AppendLine("SEQUENCEID\t0");
        sb.AppendLine($"DIFFICULT\t{chart.Difficulty:D2}");
        sb.AppendLine("LEVEL\t0.0");
        sb.AppendLine($"CREATOR\t{chart.Designer}");
        var bpm_def = chart.BpmList.BPM_DEF();
        sb.AppendLine(FormattableString.Invariant($"BPM_DEF\t{bpm_def.Item1}\t{bpm_def.Item2}\t{bpm_def.Item3}\t{bpm_def.Item4}"));
        sb.AppendLine("MET_DEF\t4\t4");
        sb.AppendLine($"RESOLUTION\t{RSL}");
        sb.AppendLine($"CLK_DEF\t{RSL}");
        sb.AppendLine("PROGJUDGE_BPM\t240.000");
        sb.AppendLine("PROGJUDGE_AER\t0.999");
        sb.AppendLine("TUTORIAL\t0");
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

        foreach (var s in chart.SflList.OrderBy(s => s.Time))
        {
            var (m, o) = Utils.BarAndTick(s.Time, RSL);
            var durTicks = Utils.Tick(s.Duration, RSL);
            sb.AppendLine(FormattableString.Invariant($"SFL\t{m}\t{o}\t{durTicks}\t{s.Multiplier:0.000000}"));
        }
        sb.AppendLine();

        foreach (var n in chart.Notes)
        {
            var line = FormatNote(n, RSL, alerts);
            if (line != null) sb.AppendLine(line);
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static readonly List<string> allowedAirColors = ["DEF", "I"]; // TODO 搞清楚UGC里的'I'颜色，在C2S里，对应的字符串是什么
    private static string AirColorTag(ChuNote n)
    {
        if (allowedAirColors.Contains(n.Tag)) return n.Tag;
        else return "DEF";
    }

    private static string FLKTag(ChuNote n) => n.Tag is "L" or "R" ? n.Tag : "L";
    
    private static bool IsSlideContinueSegments(ChuNote n) // Air Slide的前驱只能是Air Slide，反之亦然。
        => (IsSlide(n) && IsSlide(n.Previous)) || (IsAirSlide(n) && IsAirSlide(n.Previous));
    
    private static string? FormatNote(ChuNote n, int tpm, List<Alert> alerts)
    {
        var (m, o) = Utils.BarAndTick(n.Time, tpm);
        var durTicks = Utils.Tick(n.Duration, tpm);
        if (IsSlideContinueSegments(n))
        { // 特殊地，对于slide的后续段：为了保证能接上，必须保证start tick接在前一个音符的endTime的后面，duration也采用end-start的方式计算durTicks。否则可能会，因为舍入的误差，造成没有办法接起来。
            (m, o) = Utils.BarAndTick(n.Previous!.EndTime, tpm);
            durTicks = Utils.Tick(n.EndTime, tpm) - Utils.Tick(n.Previous!.EndTime, tpm);
        }
        return n.Type switch
        {
            "TAP" => $"TAP\t{m}\t{o}\t{n.Cell}\t{n.Width}",
            "CHR" => $"CHR\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.Tag}",
            "HLD" or "HXD" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{durTicks}",
            "SLD" or "SLC" or "SXD" or "SXC" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}",
            "FLK" => $"FLK\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{FLKTag(n)}",
            "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL" =>
                    $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{AirColorTag(n)}",
            "AHD" or "AHX" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{durTicks}\t{AirColorTag(n)}",
            "ASD" or "ASC" => FormatAsdAsc(n, m, o, durTicks),
            "ALD" => FormatAld(n, m, o, durTicks),
            "MNE" => $"MNE\t{m}\t{o}\t{n.Cell}\t{n.Width}",
            _ => alert(),
        };

        string? alert()
        {
            alerts.Add(new Alert(Alert.LEVEL.Warning, Locale.C2SUnknownNoteType, n.Time));
            return null;
        }
    }

    private static string FormatAsdAsc(ChuNote n, int m, int o, int durTicks)
    {
        return FormattableString.Invariant($"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{n.Height:0.#}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}\t{n.EndHeight:0.#}\t{AirColorTag(n)}");
    }

    private static string FormatAld(ChuNote n, int m, int o, int durTicks)
    {
        return FormattableString.Invariant($"ALD\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{Utils.Tick(n.CrushInterval, RSL)}\t{n.Height:0.#}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}\t{n.EndHeight:0.#}");
    }
}
