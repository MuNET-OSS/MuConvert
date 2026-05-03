using System.Text;
using MuConvert.chart;
using MuConvert.generator;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.chu;

/**
 * SUS 格式生成器。
 * 输入 IChuChart，内部自动转换后输出 SUS 文本。
 */
public class SusGenerator : IGenerator<IChuChart>
{
    private static int RSL = 480 * 4;

    public (string, List<Alert>) Generate(IChuChart chart)
    {
        var alerts = new List<Alert>();
        var sus = ConvertToSus(chart, alerts);
        var text = Serialize(sus);
        return (text, alerts);
    }

    private static SusChart ConvertToSus(IChuChart chart, List<Alert> alerts)
    {
        if (chart is SusChart sus) return sus;

        double bpm = 120.0;
        string title = "", artist = "";

        if (chart is C2sChart c2s)
        {
            bpm = c2s.BpmList.Count > 0 ? (double)c2s.BpmList[0].Bpm : 120.0;
            var result = new SusChart { Title = title, Artist = artist };
            result.BpmList.Add(new BPM(0, (decimal)bpm));
            result.Notes = c2s.Notes;
            return result;
        }

        if (chart is UgcChart ugc)
        {
            bpm = ugc.BpmList.Count > 0 ? (double)ugc.BpmList[0].Bpm : 120.0;
            var result = new SusChart { Title = ugc.Title, Artist = ugc.Artist };
            result.BpmList.Add(new BPM(0, (decimal)bpm));
            result.Notes = ugc.Notes;
            return result;
        }

        alerts.Add(new Alert(Error, string.Format(Locale.ChuGeneratorUnsupported, "→ SUS")));
        throw new ConversionException(alerts);
    }

    private static string Serialize(SusChart sus)
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
