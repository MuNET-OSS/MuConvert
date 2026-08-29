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
public class C2sParser: BaseChuParser
{
    private int RSL = 384;
    private static readonly HashSet<string> HeadTags = new(StringComparer.OrdinalIgnoreCase)
        { "VERSION", "MUSIC", "SEQUENCEID", "DIFFICULT", "LEVEL", "CREATOR", "BPM_DEF", "MET_DEF", "RESOLUTION", "CLK_DEF", "PROGJUDGE_BPM", "PROGJUDGE_AER", "TUTORIAL" };
    private static readonly HashSet<string> TimingTags = new(StringComparer.OrdinalIgnoreCase)
        { "BPM", "MET", "SFL" };

    // C2S 会原始记录 targetNote 字符串；用于在 Previous 推断有多个候选时优先匹配。
    private readonly Dictionary<ChuNote, string> _rawTargetNote = new();

    public override (ChuChart, List<Alert>) Parse(string text)
    {
        var chart = new ChuChart();
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

        FillAllPrevious(chart, alerts, _rawTargetNote);
        chart.Sort();
        return (chart, alerts);
    }

    private void ParseHeader(string[] p, ChuChart chart)
    {
        var tag = p[0].ToUpperInvariant();
        switch (tag)
        {
            case "MUSIC": chart.MusicId = Int(p, 1).ToString(); break;
            case "DIFFICULT": chart.Difficulty = Int(p, 1); break;
            case "CREATOR": chart.Designer = Str(p, 1); break;
            case "RESOLUTION": RSL = Math.Max(1, Int(p, 1, 384)); break;
        }
    }

    private void ParseTiming(string[] p, ChuChart chart)
    {
        var tag = p[0].ToUpperInvariant();
        switch (tag)
        {
            case "BPM":
                chart.BpmList.Add(new BPM(Int(p, 1) + new Rational(Int(p, 2), RSL), 
                    decimal.Parse(p[3], CultureInfo.InvariantCulture)));
                break;
            case "MET":
                chart.MetList.Add(new MET(Int(p, 1) + new Rational(Int(p, 2), RSL), Int(p, 4, 4), Int(p, 3, 4)));
                break;
            case "SFL":
                chart.SflList.Add((
                    Int(p, 1) + new Rational(Int(p, 2), RSL),
                    new Rational(Int(p, 3), RSL),
                    decimal.Parse(p[4], CultureInfo.InvariantCulture)));
                break;
        }
    }

    private void ParseNote(string[] p, ChuChart chart, List<Alert> alerts, int lineNum)
    {
        var type = p[0].ToUpperInvariant();
        var note = new ChuNote { Type = type, Time = Int(p, 1) + new Rational(Int(p, 2), RSL) };
        string? targetNote = null;

        switch (type)
        {
            case "TAP": case "MNE":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); break;
            case "CHR":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.Tag = Str(p, 5); break;
            case "HLD":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); 
                note.Duration = new Rational(Int(p, 5), RSL); 
                break;
            case "HXD":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); 
                note.Duration = new Rational(Int(p, 5), RSL); 
                note.Tag = Str(p, 6);
                break;
            case "SLD": case "SLC":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                note.Duration = new Rational(Int(p, 5), RSL);
                note.EndCell = Int(p, 6); note.EndWidth = Math.Max(1, Int(p, 7, 1));
                break;
            case "SXD": case "SXC":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                note.Duration = new Rational(Int(p, 5), RSL);
                note.EndCell = Int(p, 6); note.EndWidth = Math.Max(1, Int(p, 7, 1));
                note.Tag = Str(p, 9);
                break;
            case "FLK":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); note.Tag = Str(p, 5); break;
            case "AIR": case "AUR": case "AUL": case "ADW": case "ADR": case "ADL":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1)); targetNote = Str(p, 5);
                if (p.Length >= 7) note.Tag = Str(p, 6);
                break;
            case "AHD": case "AHX":
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                targetNote = Str(p, 5); note.Duration = new Rational(Int(p, 6), RSL);
                if (p.Length >= 8) note.Tag = Str(p, 7);
                break;
            case "ASD": case "ASC":
                // 文档：M O Cell Width | TargetNote | 未知 | Duration | EndCell | EndWidth | 未知 | Tag
                if (p.Length < 12)
                {
                    alerts.Add(new Alert(Warning, $"{type} 列数不足（期望至少 12 列）") { Line = lineNum });
                    return;
                }
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                targetNote = Str(p, 5);
                note.Height = Decimal(p, 6, 5); note.EndHeight = Decimal(p, 10, 5);
                note.Duration = new Rational(Int(p, 7), RSL);
                note.EndCell = Int(p, 8); note.EndWidth = Math.Max(1, Int(p, 9, 1));
                note.Tag = Str(p, 11);
                break;
            case "ALD":
                // 根据 https://github.com/MuNET-OSS/MuConvert/pull/3#issuecomment-4405859671 实现
                if (p.Length < 11)
                {
                    alerts.Add(new Alert(Warning, "ALD 列数不足（期望至少 11 列）") { Line = lineNum });
                    return;
                }
                note.Cell = Int(p, 3); note.Width = Math.Max(1, Int(p, 4, 1));
                note.CrushInterval = new Rational(Int(p, 5), RSL);
                note.Height = Decimal(p, 6, 5); note.EndHeight = Decimal(p, 10, 5);
                note.Duration = new Rational(Int(p, 7), RSL);
                note.EndCell = Int(p, 8); note.EndWidth = Math.Max(1, Int(p, 9, 1));
                note.Tag = Str(p, 11);
                break;
            default:
                alerts.Add(new Alert(Warning, string.Format(Locale.C2SUnknownNoteType, type)) { Line = lineNum }); return;
        }

        if (targetNote != null) _rawTargetNote[note] = targetNote;
        chart.Notes.Add(note);
    }

    private static int Int(string[] p, int i, int def = 0) => i < p.Length && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    private static decimal Decimal(string[] p, int i, decimal def = 0) => i < p.Length && decimal.TryParse(p[i], CultureInfo.InvariantCulture, out var v) ? v : def;
    private static string Str(string[] p, int i) => i < p.Length ? p[i] : "";
}
