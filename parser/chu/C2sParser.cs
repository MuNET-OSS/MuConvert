using System.Globalization;
using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using Rationals;
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
            case "MUSIC": chart.MusicId = Int(p, 1).ToString(); break;
            case "DIFFICULT": chart.Difficulty = Int(p, 1); break;
            case "CREATOR": chart.Designer = Str(p, 1); break;
            case "RESOLUTION": chart.Resolution = Math.Max(1, Int(p, 1, 384)); break;
        }
    }

    private static void ParseTiming(string[] p, C2sChart chart)
    {
        var tag = p[0].ToUpperInvariant();
        var tpm = chart.Resolution;
        switch (tag)
        {
            case "BPM":
                chart.BpmList.Add(new BPM(Int(p, 1) + new Rational(Int(p, 2), tpm), decimal.Parse(p[3])));
                break;
            case "MET":
                chart.MetList.Add(new MET(Int(p, 1) + new Rational(Int(p, 2), tpm), Int(p, 4, 4), Int(p, 3, 4)));
                break;
            case "SFL":
                chart.SflList.Add((
                    Int(p, 1) + new Rational(Int(p, 2), tpm),
                    new Rational(Int(p, 3), tpm),
                    decimal.Parse(p[4])));
                break;
        }
    }

    private static void ParseNote(string[] p, C2sChart chart, List<Alert> alerts, int lineNum)
    {
        var tag = p[0].ToUpperInvariant();
        var tpm = chart.Resolution;
        var note = new ChuNote { Type = tag, Time = Int(p, 1) + new Rational(Int(p, 2), tpm) };

        switch (tag)
        {
            case "TAP": case "MNE":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); break;
            case "CHR":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.Tag = Str(p, 5); break;
            case "HLD": case "HXD":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.Duration = new Rational(Int(p, 5), tpm); break;
            case "SLD": case "SLC": case "SXD": case "SXC":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                note.Duration = new Rational(Int(p, 5), tpm);
                note.EndCell = Int(p, 6); note.EndWidth = Math.Max(1, Int(p, 7, 1));
                break;
            case "FLK":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.Tag = Str(p, 5); break;
            case "AIR": case "AUR": case "AUL": case "ADW": case "ADR": case "ADL":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.TargetNote = Str(p, 5);
                if (p.Length >= 7) note.Tag = Str(p, 6);
                break;
            case "AHD": case "AHX":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                note.TargetNote = Str(p, 5); note.Duration = new Rational(Int(p, 6), tpm);
                if (p.Length >= 8) note.Tag = Str(p, 7);
                break;
            case "ASD": case "ASC":
                // 文档：M O Cell Width | TargetNote | 未知 | Duration | EndCell | EndWidth | 未知 | Tag
                if (p.Length < 12)
                {
                    alerts.Add(new Alert(Warning, $"{tag} 列数不足（期望至少 12 列）") { Line = lineNum });
                    return;
                }
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                note.TargetNote = Str(p, 5);
                note.ExtraData = [Int(p, 6), Int(p, 10)];
                note.Duration = new Rational(Int(p, 7), tpm);
                note.EndCell = Int(p, 8); note.EndWidth = Math.Max(1, Int(p, 9, 1));
                note.Tag = Str(p, 11);
                break;
            case "ALD":
                // 文档：M O Cell Width | 未知×3 | EndCell | EndWidth | 未知（1 或 3）
                if (p.Length < 11)
                {
                    alerts.Add(new Alert(Warning, "ALD 列数不足（期望至少 11 列）") { Line = lineNum });
                    return;
                }
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                note.ExtraData = [Int(p, 5), Int(p, 6), Int(p, 7), Int(p, 10)];
                note.EndCell = Int(p, 8); note.EndWidth = Math.Max(1, Int(p, 9, 1));
                break;
            default:
                alerts.Add(new Alert(Warning, string.Format(Locale.C2SUnknownNoteType, tag)) { Line = lineNum }); return;
        }

        chart.Notes.Add(note);
    }

    private static int Int(string[] p, int i, int def = 0) => i < p.Length && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    private static string Str(string[] p, int i) => i < p.Length ? p[i] : "";
}
