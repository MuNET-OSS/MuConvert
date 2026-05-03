using System.Text;
using MuConvert.generator;
using MuConvert.utils;

namespace MuConvert.chu;

public class SusGenerator : IGenerator<ChuChart>
{
    private static int RSL = 480 * 4;

    public (string, List<Alert>) Generate(ChuChart chart)
    {
        var alerts = new List<Alert>();
        var text = Serialize(chart);
        return (text, alerts);
    }

    private static string Serialize(ChuChart sus)
    {
        sus.Sort();
        
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(sus.Title)) sb.AppendLine($"#TITLE \"{sus.Title}\"");
        if (!string.IsNullOrEmpty(sus.Artist)) sb.AppendLine($"#ARTIST \"{sus.Artist}\"");
        if (!string.IsNullOrEmpty(sus.Designer)) sb.AppendLine($"#DESIGNER \"{sus.Designer}\"");
        sb.AppendLine($"#BPM_DEF {sus.StartBpm:F2}");
        sb.AppendLine($"#REQUEST \"{RSL / 4}\"");
        sb.AppendLine();

        foreach (var n in sus.Notes)
        {
            var (m, o) = Utils.BarAndTick(n.Time, RSL);
            sb.AppendLine($"#{m:X2}{o:X3}:{FormatData(n, RSL)}");
        }

        return sb.ToString();
    }

    private static string FormatData(ChuNote n, int tpm)
    {
        string lw = $"{n.Cell*2:X2}{n.Width*2:X2}";
        string tc = TypeCode(n.Type);
        var durTicks = Utils.Tick(n.Duration, tpm);
        string dur = $"{durTicks:X4}";
        return tc switch
        {
            "01" or "02" or "03" or "10" => $"{tc}{lw}",
            "05" or "08" => $"{tc}{lw}{dur}",
            "06" => $"{tc}{lw}{dur}{n.EndCell*2:X2}{n.EndWidth*2:X2}",
            "07" or "09" => $"{tc}{lw}{n.TargetNote}",
            _ => $"01{lw}"
        };
    }

    private static string TypeCode(string t) => t switch
    {
        "TAP" => "01", "CHR" => "02", "FLK" => "03",
        "HLD" => "05", "SLD" => "06", "SLC" => "06",
        "AIR" => "07", "AUR" => "07", "AUL" => "07",
        "AHD" => "08", "AHX" => "08", "ADW" => "09", "ADR" => "09", "ADL" => "09",
        "MNE" => "10", _ => "01"
    };
}
