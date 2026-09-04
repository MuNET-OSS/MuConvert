using System.Globalization;
using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;
using SegDictKey = (MuConvert.chu.ChuNoteType Type, bool IsAir, Rationals.Rational Time, int Cell, int Width);

namespace MuConvert.chu;

/**
 * SUS 格式解析器（社区工具格式，REQUEST=480 tick/拍，lane 0–31）。
 * #MMTT:data 十六进制编码音符。
 */
public class SusParser: BaseChuParser
{
    private int RSL = 480 * 4;

    private readonly Dictionary<SegDictKey, List<ChuNote>> segDict = new();
    private readonly Dictionary<ChuNote, string> _rawTargetNote = new();

    public override (ChuChart, List<Alert>) Parse(string text)
    {
        var chart = new ChuChart();
        var alerts = new List<Alert>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (!line.StartsWith('#'))
            {
                alerts.Add(new Alert(Warning, $"意外的行（不以 # 开头）: {line}") { Line = i + 1 });
                continue;
            }

            var content = line[1..];

            if (IsHeaderLine(content))
            {
                ParseHeaderLine(content, chart, alerts, i + 1);
            }
            else
            {
                ParseNoteLine(content, chart, alerts, i + 1);
            }
        }

        FillAllPrevious(chart, alerts, _rawTargetNote);
        chart.Sort();
        return (chart, alerts);
    }

    private static bool IsHeaderLine(string content)
    {
        return content.StartsWith("TITLE ")
               || content.StartsWith("ARTIST ")
               || content.StartsWith("DESIGNER ")
               || content.StartsWith("BPM_DEF ")
               || content.StartsWith("REQUEST ");
    }

    private void ParseHeaderLine(string content, ChuChart chart, List<Alert> alerts, int lineNum)
    {
        if (content.StartsWith("TITLE "))
        {
            chart.Title = Unquote(content[6..]);
        }
        else if (content.StartsWith("ARTIST "))
        {
            chart.Artist = Unquote(content[7..]);
        }
        else if (content.StartsWith("DESIGNER "))
        {
            chart.Designer = Unquote(content[9..]);
        }
        else if (content.StartsWith("BPM_DEF "))
        {
            var bpmStr = content[8..].Trim().Trim('"');
            if (double.TryParse(bpmStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm))
                chart.BpmList.Add(new BPM(0, (decimal)bpm));
            else
                alerts.Add(new Alert(Warning, $"BPM_DEF 格式错误: {content}") { Line = lineNum });
        }
        else if (content.StartsWith("REQUEST "))
        {
            var reqStr = content[8..].Trim().Trim('"');
            if (int.TryParse(reqStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                RSL = ticks * 4;
            else
                alerts.Add(new Alert(Warning, $"REQUEST 格式错误: {content}") { Line = lineNum });
        }
    }

    private void ParseNoteLine(string content, ChuChart chart, List<Alert> alerts, int lineNum)
    {
        var colonIdx = content.IndexOf(':');
        if (colonIdx < 0)
        {
            alerts.Add(new Alert(Warning, $"音符行缺少冒号: {content}") { Line = lineNum });
            return;
        }

        var timingStr = content[..colonIdx];
        var dataStr = content[(colonIdx + 1)..];

        if (timingStr.Length < 5)
        {
            alerts.Add(new Alert(Warning, $"音符行时序部分过短: {content}") { Line = lineNum });
            return;
        }

        var measure = HexToInt(timingStr[..2]);
        var tick = HexToInt(timingStr[2..5]);

        if (dataStr.Length < 6)
        {
            alerts.Add(new Alert(Warning, $"音符行数据部分过短: {content}") { Line = lineNum });
            return;
        }

        var typeCode = HexToInt(dataStr[..2]);
        var lane = HexToInt(dataStr[2..4]);
        var width = HexToInt(dataStr[4..6]);

        ChuNote? note = new ChuNote
        {
            Time = measure + new Rational(tick, RSL),
            Cell = lane / 2,
            Width = Math.Max(1, width / 2),
        };

        switch (typeCode)
        {
            case 0x01: // TAP
                note.Type = ChuNoteType.Tap;
                break;

            case 0x02: // CHR / ExTap（SUS 不编码方向，给一个缺省 Ex）
                note.Type = ChuNoteType.Tap;
                note.Ex = ExDirection.UP;
                break;

            case 0x03: // FLK
                note.Type = ChuNoteType.Flick;
                break;

            case 0x10: // MNE
                note.Type = ChuNoteType.Mine;
                break;

            case 0x07: // AIR
            case 0x09: // ADW
                note.Type = ChuNoteType.Tap;
                note.IsAir = true;
                note.AirDirection = typeCode == 0x07 ? AirDirection.AIR : AirDirection.ADW;
                if (dataStr.Length < 8)
                    alerts.Add(new Alert(Warning, $"AIR/ADW 音符缺少目标: {dataStr}") { Line = lineNum, RelevantNote = FormatNoteRef(note) });
                else if (dataStr.Length > 6)
                    _rawTargetNote[note] = dataStr[6..];
                break;

            case 0x05: // HLD
            case 0x08: // AHD
            case 0x06: // SLD
                note.Type = typeCode == 0x06 ? ChuNoteType.Slide : ChuNoteType.Hold;
                note.IsAir = typeCode == 0x08;
                note = ParseSustainedNote(dataStr, note, alerts, lineNum);
                break;

            default:
                alerts.Add(new Alert(Warning, $"未知的音符类型码 0x{typeCode:X2}: {content}") { Line = lineNum });
                return;
        }

        if (note != null) chart.Notes.Add(note);
    }

    /**
     * 解析 HLD/AHD/SLD：时长写入 Segments；若与已有同类型音符首尾相接，则并入同一 ChuNote。
     */
    private ChuNote? ParseSustainedNote(string dataStr, ChuNote note, List<Alert> alerts, int lineNum)
    {
        SegDictKey segKey = (note.Type, note.IsAir, note.Time, note.Cell, note.Width);
        bool isConnect = false;
        if (segDict.Remove(segKey, out ChuNote existing))
        {
            note = existing;
            isConnect = true;
        }

        if (dataStr.Length < 10)
        {
            alerts.Add(new Alert(Warning, $"{(note.Type == ChuNoteType.Slide ? "SLD" : note.IsAir ? "AHD" : "HLD")} 音符缺少时长: {dataStr}")
            {
                Line = lineNum,
                RelevantNote = FormatNoteRef(note),
            });
            return isConnect ? null : note;
        }

        var seg = new ChuSegment(note)
        {
            Length = new Rational(HexToInt(dataStr[6..10]), RSL),
        };
        if (note.Type == ChuNoteType.Slide && dataStr.Length >= 14)
        {
            seg.EndCell = HexToInt(dataStr[10..12]) / 2;
            seg.EndWidth = Math.Max(1, HexToInt(dataStr[12..14]) / 2);
        }
        note.Segments.Add(seg);
        segDict.Add((note.Type, note.IsAir, note.EndTime, note.EndCell, note.EndWidth), note);

        return isConnect ? null : note;
    }

    private static int HexToInt(string hex) =>
        int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static string Unquote(string s)
    {
        var trimmed = s.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1];
        return trimmed;
    }

    private string FormatNoteRef(ChuNote note)
    {
        var (m, o) = Utils.BarAndTick(note.Time, RSL);
        return $"#{m:X2}{o:X3}:{note.Type}";
    }
}
