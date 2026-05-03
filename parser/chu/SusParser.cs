using System.Globalization;
using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.chu;

/**
 * SUS 格式解析器（社区工具格式，REQUEST=480 tick/拍，lane 0–31）。
 * #MMTT:data 十六进制编码音符。
 */
public class SusParser : IParser<ChuChart>
{
    private static int RSL = 480 * 4;
    
    private static readonly Dictionary<int, string> TypeMap = new()
    {
        [0x01] = "TAP",
        [0x02] = "CHR",
        [0x03] = "FLK",
        [0x05] = "HLD",
        [0x06] = "SLD",
        [0x07] = "AIR",
        [0x08] = "AHD",
        [0x09] = "ADW",
        [0x10] = "MNE",
    };

    public (ChuChart, List<Alert>) Parse(string text)
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

    private static void ParseHeaderLine(string content, ChuChart chart, List<Alert> alerts, int lineNum)
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

    private static void ParseNoteLine(string content, ChuChart chart, List<Alert> alerts, int lineNum)
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

        if (!TypeMap.TryGetValue(typeCode, out var typeName))
        {
            alerts.Add(new Alert(Warning, $"未知的音符类型码 0x{typeCode:X2}: {content}") { Line = lineNum });
            return;
        }

        var note = new ChuNote
        {
            Type = typeName,
            Time = measure + new Rational(tick, RSL),
            Cell = lane / 2,
            Width = Math.Max(1, width / 2),
        };

        switch (note.Type)
        {
            case "TAP":
            case "CHR":
            case "FLK":
            case "MNE":
                break;

            case "HLD":
                ParseHoldData(dataStr, note, RSL, alerts, lineNum);
                break;

            case "SLD":
                ParseSlideData(dataStr, note, RSL, alerts, lineNum);
                break;

            case "AIR":
            case "ADW":
                ParseAirTarget(dataStr, note, RSL, alerts, lineNum);
                break;

            case "AHD":
                ParseAhdData(dataStr, note, RSL, alerts, lineNum);
                break;
        }

        chart.Notes.Add(note);
    }

    private static void ParseHoldData(string dataStr, ChuNote note, int tpm, List<Alert> alerts, int lineNum)
    {
        if (dataStr.Length >= 10)
        {
            note.Duration = new Rational(HexToInt(dataStr[6..10]), tpm);
        }
        else
        {
            alerts.Add(new Alert(Warning, $"HLD 音符缺少时长: {dataStr}") { Line = lineNum, RelevantNote = FormatNoteRef(note, tpm) });
        }
    }

    private static void ParseSlideData(string dataStr, ChuNote note, int tpm, List<Alert> alerts, int lineNum)
    {
        if (dataStr.Length >= 10)
        {
            note.Duration = new Rational(HexToInt(dataStr[6..10]), tpm);
        }
        else
        {
            alerts.Add(new Alert(Warning, $"SLD 音符缺少时长: {dataStr}") { Line = lineNum, RelevantNote = FormatNoteRef(note, tpm) });
            return;
        }

        if (dataStr.Length >= 14)
        {
            note.EndCell = HexToInt(dataStr[10..12]) / 2;
            note.EndWidth = Math.Max(1, HexToInt(dataStr[12..14]) / 2);
        }
    }

    private static void ParseAirTarget(string dataStr, ChuNote note, int tpm, List<Alert> alerts, int lineNum)
    {
        if (dataStr.Length >= 8)
        {
            note.TargetNote = HexToInt(dataStr[6..8]).ToString();
        }
        else
        {
            alerts.Add(new Alert(Warning, $"AIR/ADW 音符缺少目标: {dataStr}") { Line = lineNum, RelevantNote = FormatNoteRef(note, tpm) });
        }
    }

    private static void ParseAhdData(string dataStr, ChuNote note, int tpm, List<Alert> alerts, int lineNum)
    {
        if (dataStr.Length >= 10)
        {
            note.Duration = new Rational(HexToInt(dataStr[6..10]), tpm);
        }
        else
        {
            alerts.Add(new Alert(Warning, $"AHD 音符缺少时长: {dataStr}") { Line = lineNum, RelevantNote = FormatNoteRef(note, tpm) });
        }
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

    private static string FormatNoteRef(ChuNote note, int tpm)
    {
        var (m, o) = Utils.BarAndTick(note.Time, tpm);
        return $"#{m:X2}{o:X3}:{note.Type}";
    }
}
