using System.Text;
using MuConvert.generator;
using MuConvert.utils;
using static MuConvert.utils.ChuUtils;

namespace MuConvert.chu;

public class SusGenerator : IGenerator<ChuChart>
{
    private int RSL = 480 * 4;

    public (string, List<Alert>) Generate(ChuChart chart)
    {
        var alerts = new List<Alert>();
        var text = Serialize(chart, alerts);
        return (text, alerts);
    }

    private string Serialize(ChuChart sus, List<Alert> alerts)
    {
        sus.Sort();
        
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(sus.Title)) sb.AppendLine($"#TITLE \"{sus.Title}\"");
        if (!string.IsNullOrEmpty(sus.Artist)) sb.AppendLine($"#ARTIST \"{sus.Artist}\"");
        if (!string.IsNullOrEmpty(sus.Designer)) sb.AppendLine($"#DESIGNER \"{sus.Designer}\"");
        sb.AppendLine(FormattableString.Invariant($"#BPM_DEF {sus.StartBpm:F2}"));
        sb.AppendLine($"#REQUEST \"{RSL / 4}\"");
        sb.AppendLine();

        foreach (var n in sus.Notes)
        {
            foreach (var line in FormatNote(n, alerts))
                sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private List<string> FormatNote(ChuNote n, List<Alert> alerts)
    {
        List<string> results = [];

        if (n.Type is ChuNoteType.Tap or ChuNoteType.Flick or ChuNoteType.Mine)
        {
            var (m, o) = Utils.BarAndTick(n.Time, RSL);
            var lw = $"{n.Cell * 2:X2}{n.Width * 2:X2}";
            var tc = TypeCode(n);
            if (n.IsAir)
            {
                var targetStr = AsC2sPreviousStr(n.TargetNote) ?? "N";
                results.Add($"#{m:X2}{o:X3}:{tc}{lw}{targetStr}");
            }
            else
            {
                results.Add($"#{m:X2}{o:X3}:{tc}{lw}");
            }
        }
        else if (n.Type is ChuNoteType.Hold or ChuNoteType.Slide)
        {
            // SUS 不支持 Air-Slide；按段展开（与 C2s 一样，多段持续性音符拆成多行）
            if (n is { IsAir: true, Type: ChuNoteType.Slide })
            {
                alerts.Add(new Alert(Alert.LEVEL.Warning, "SUS 不支持 Air-Slide，已跳过", n.Time));
                return results;
            }

            var start = (n.Time, n.Cell, n.Width);
            foreach (var seg in n.Segments)
            {
                var (m, o) = Utils.BarAndTick(start.Time, RSL);
                var lw = $"{start.Cell * 2:X2}{start.Width * 2:X2}";
                var tc = TypeCode(n);
                // 用 end-start 的 tick 差，避免分段舍入导致接不上
                var endTime = start.Time + seg.Length;
                var durTicks = Utils.Tick(endTime, RSL) - Utils.Tick(start.Time, RSL);
                var dur = $"{durTicks:X4}";

                if (n.Type == ChuNoteType.Slide)
                    results.Add($"#{m:X2}{o:X3}:{tc}{lw}{dur}{seg.EndCell * 2:X2}{seg.EndWidth * 2:X2}");
                else
                    results.Add($"#{m:X2}{o:X3}:{tc}{lw}{dur}");

                start = (endTime, seg.EndCell, seg.EndWidth);
            }
        }
        else
        {
            alerts.Add(new Alert(Alert.LEVEL.Warning, $"SUS 不支持的音符类型: {n.Type}", n.Time));
        }

        return results;
    }

    private static string TypeCode(ChuNote n) => (n.Type, n.IsAir) switch
    {
        (ChuNoteType.Tap, false) => n.IsEx ? "02" : "01",
        (ChuNoteType.Flick, _) => "03",
        (ChuNoteType.Hold, false) => "05",
        (ChuNoteType.Slide, false) => "06",
        (ChuNoteType.Tap, true) => IsAirDown(n) ? "09" : "07",
        (ChuNoteType.Hold, true) => "08",
        (ChuNoteType.Mine, _) => "10",
        _ => "01",
    };
}
