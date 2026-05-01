using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.chu;

/**
 * SUS 格式解析器（社区工具格式，REQUEST=480 tick/拍，lane 0–31）。
 * #MMTT:data 十六进制编码音符。
 */
public class SusParser : IParser<SusChart>
{
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
        [0x0A] = "MNE",
    };

    public (SusChart, List<Alert>) Parse(string text)
    {
        var chart = new SusChart();
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

    private static void ParseHeaderLine(string content, SusChart chart, List<Alert> alerts, int lineNum)
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
            if (double.TryParse(bpmStr, out var bpm))
                chart.Bpm = bpm;
            else
                alerts.Add(new Alert(Warning, $"BPM_DEF 格式错误: {content}") { Line = lineNum });
        }
        else if (content.StartsWith("REQUEST "))
        {
            var reqStr = content[8..].Trim().Trim('"');
            if (int.TryParse(reqStr, out var ticks))
                chart.TicksPerBeat = ticks;
            else
                alerts.Add(new Alert(Warning, $"REQUEST 格式错误: {content}") { Line = lineNum });
        }
    }

    private static void ParseNoteLine(string content, SusChart chart, List<Alert> alerts, int lineNum)
    {
        var colonIdx = content.IndexOf(':');
        if (colonIdx < 0)
        {
            alerts.Add(new Alert(Warning, $"音符行缺少冒号: {content}") { Line = lineNum });
            return;
        }

        var timingStr = content[..colonIdx];
        var dataStr = content[(colonIdx + 1)..];

        if (timingStr.Length < 4)
        {
            alerts.Add(new Alert(Warning, $"音符行时序部分过短: {content}") { Line = lineNum });
            return;
        }

        var measure = HexToInt(timingStr[..2]);
        var tick = HexToInt(timingStr[2..4]);

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
            Measure = measure,
            Offset = tick,
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
                ParseHoldData(dataStr, note, alerts, lineNum);
                break;

            case "SLD":
                ParseSlideData(dataStr, note, alerts, lineNum);
                break;

            case "AIR":
            case "ADW":
                ParseAirTarget(dataStr, note, alerts, lineNum);
                break;

            case "AHD":
                ParseAhdData(dataStr, note, alerts, lineNum);
                break;
        }

        chart.Notes.Add(note);
    }

    private static void ParseHoldData(string dataStr, ChuNote note, List<Alert> alerts, int lineNum)
    {
        if (dataStr.Length >= 10)
        {
            note.HoldDuration = HexToInt(dataStr[6..10]);
        }
        else
        {
            alerts.Add(new Alert(Warning, $"HLD 音符缺少时长: {dataStr}") { Line = lineNum, RelevantNote = FormatNoteRef(note) });
        }
    }

    private static void ParseSlideData(string dataStr, ChuNote note, List<Alert> alerts, int lineNum)
    {
        if (dataStr.Length >= 10)
        {
            note.SlideDuration = HexToInt(dataStr[6..10]);
        }
        else
        {
            alerts.Add(new Alert(Warning, $"SLD 音符缺少时长: {dataStr}") { Line = lineNum, RelevantNote = FormatNoteRef(note) });
            return;
        }

        if (dataStr.Length >= 14)
        {
            note.EndCell = HexToInt(dataStr[10..12]) / 2;
            note.EndWidth = Math.Max(1, HexToInt(dataStr[12..14]) / 2);
        }
    }

    private static void ParseAirTarget(string dataStr, ChuNote note, List<Alert> alerts, int lineNum)
    {
        if (dataStr.Length >= 8)
        {
            note.TargetNote = HexToInt(dataStr[6..8]).ToString();
        }
        else
        {
            alerts.Add(new Alert(Warning, $"AIR/ADW 音符缺少目标: {dataStr}") { Line = lineNum, RelevantNote = FormatNoteRef(note) });
        }
    }

    private static void ParseAhdData(string dataStr, ChuNote note, List<Alert> alerts, int lineNum)
    {
        if (dataStr.Length >= 10)
        {
            note.AirHoldDuration = HexToInt(dataStr[6..10]);
        }
        else
        {
            alerts.Add(new Alert(Warning, $"AHD 音符缺少时长: {dataStr}") { Line = lineNum, RelevantNote = FormatNoteRef(note) });
        }
    }

    private static int HexToInt(string hex)
    {
        if (hex.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f'))
            return Convert.ToInt32(hex, 16);
        return 0;
    }

    private static string Unquote(string s)
    {
        var trimmed = s.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1];
        return trimmed;
    }

    private static string FormatNoteRef(ChuNote note)
    {
        return $"#{note.Measure:X2}{note.Offset:X2}:{note.Type}";
    }
}
