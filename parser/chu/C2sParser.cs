using System.Globalization;
using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.chu;

/**
 * C2S 格式解析器（官方格式，RESOLUTION=384 tick/小节）。
 * Tab 分隔文本，识别 HEADER / TIMING / NOTES 区段。
 */
public class C2sParser : IParser<C2sChart>
{
    private static readonly HashSet<string> HeadTags = new(StringComparer.OrdinalIgnoreCase)
        { "VERSION", "MUSIC", "SEQUENCEID", "DIFFICULT", "LEVEL", "CREATOR", "BPM_DEF", "MET_DEF", "RESOLUTION", "CLK_DEF", "PROGJUDGE_BPM", "PROGJUDGE_AER", "TUTORIAL" };
    private static readonly HashSet<string> TimingTags = new(StringComparer.OrdinalIgnoreCase)
        { "BPM", "MET", "SFL" };

    public (C2sChart, List<Alert>) Parse(string text)
    {
        var chart = new C2sChart();
        var alerts = new List<Alert>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        bool inNotes = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("T_")) continue;

            var parts = line.Split('\t');
            var tag = parts[0].ToUpperInvariant();

            if (inNotes || !HeadTags.Contains(tag) && !TimingTags.Contains(tag))
            {
                inNotes = true;
                ParseNote(parts, chart, alerts, i + 1);
            }
            else if (HeadTags.Contains(tag))
            {
                ParseHeader(parts, chart);
            }
            else if (TimingTags.Contains(tag))
            {
                ParseTiming(parts, chart);
                inNotes = false;
            }
        }

        return (chart, alerts);
    }

    private static void ParseHeader(string[] p, C2sChart chart)
    {
        var tag = p[0].ToUpperInvariant();
        switch (tag)
        {
            case "VERSION": chart.Version = Str(p, 1); break;
            case "MUSIC": chart.MusicId = Int(p, 1); break;
            case "DIFFICULT": chart.DifficultId = Int(p, 1); break;
            case "CREATOR": chart.Creator = Str(p, 1); break;
            case "BPM_DEF": chart.DefBpm = Dbl(p, 1, 120.0); break;
            case "RESOLUTION": chart.Resolution = Math.Max(1, Int(p, 1, 384)); break;
        }
    }

    private static void ParseTiming(string[] p, C2sChart chart)
    {
        var tag = p[0].ToUpperInvariant();
        switch (tag)
        {
            case "BPM":
                chart.BpmEvents.Add((Int(p, 1), Int(p, 2), Dbl(p, 3, 120.0)));
                break;
            case "MET":
                chart.MetEvents.Add((Int(p, 1), Int(p, 2), Int(p, 3, 4), Int(p, 4, 4)));
                break;
            case "SFL":
                chart.SflEvents.Add((Int(p, 1), Int(p, 2), Int(p, 3), Dbl(p, 4, 1.0)));
                break;
        }
    }

    private static void ParseNote(string[] p, C2sChart chart, List<Alert> alerts, int lineNum)
    {
        var tag = p[0].ToUpperInvariant();
        var note = new ChuNote { Type = tag, Measure = Int(p, 1), Offset = Int(p, 2) };

        switch (tag)
        {
            case "TAP": case "MNE":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); break;
            case "CHR":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.Tag = Str(p, 5); break;
            case "HLD": case "HXD":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.HoldDuration = Int(p, 5); break;
            case "SLD": case "SLC": case "SXD": case "SXC":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                note.SlideDuration = Int(p, 5); note.EndCell = Int(p, 6); note.EndWidth = Math.Max(1, Int(p, 7, 1)); break;
            case "FLK":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.Tag = Str(p, 5); break;
            case "AIR": case "AUR": case "AUL": case "ADW": case "ADR": case "ADL":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.TargetNote = Str(p, 5); break;
            case "AHD":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                note.TargetNote = Str(p, 5); note.AirHoldDuration = Int(p, 6); break;
            case "ALD": case "ASD":
                note.StartHeight = Int(p, 3); note.SlideDuration = Int(p, 4);
                note.EndCell = Int(p, 5); note.EndWidth = Math.Max(1, Int(p, 6, 1));
                note.TargetHeight = Int(p, 7); note.NoteColor = Str(p, 8); break;
            default:
                alerts.Add(new Alert(Warning, string.Format(Locale.C2SUnknownNoteType, tag)) { Line = lineNum }); return;
        }

        chart.Notes.Add(note);
    }

    private static int Int(string[] p, int i, int def = 0) => i < p.Length && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    private static double Dbl(string[] p, int i, double def = 0) => i < p.Length && double.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
    private static string Str(string[] p, int i) => i < p.Length ? p[i] : "";
}
