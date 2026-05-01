using System.Globalization;
using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.chu;

/**
 * UGC 格式解析器（UMIGURI 格式，@TICKS=480 tick/拍）。
 * @HEADER 标签 + #measure'tick:code 音符格式。
 */
public class UgcParser : IParser<UgcChart>
{
    private static readonly Dictionary<string, string> AirDirections = new()
    {
        ["UC"] = "AIR",
        ["UR"] = "AUR",
        ["UL"] = "AUL",
        ["DC"] = "ADW",
        ["DR"] = "ADR",
        ["DL"] = "ADL",
        ["HD"] = "AHD",
    };

    private static readonly Dictionary<string, string> ChrExtras = new()
    {
        ["U"] = "UP",
        ["D"] = "DW",
        ["C"] = "CE",
    };

    public (UgcChart, List<Alert>) Parse(string text)
    {
        var chart = new UgcChart();
        var alerts = new List<Alert>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inHeader = true;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (inHeader)
            {
                if (line == "@ENDHEAD")
                {
                    inHeader = false;
                    continue;
                }
                ParseHeaderLine(line, chart, alerts, i + 1);
            }
            else
            {
                i = ParseNoteLine(lines, i, chart, alerts);
            }
        }

        return (chart, alerts);
    }

    private static void ParseHeaderLine(string line, UgcChart chart, List<Alert> alerts, int lineNum)
    {
        if (!line.StartsWith('@'))
        {
            alerts.Add(new Alert(Warning, $"意外的非头部行: {line}") { Line = lineNum });
            return;
        }

        var spaceIdx = line.IndexOf('\t');
        var tag = spaceIdx > 0 ? line[..spaceIdx] : line;
        var value = spaceIdx > 0 ? line[(spaceIdx + 1)..].Trim() : "";

        switch (tag)
        {
            case "@VER":
                chart.Version = value;
                break;

            case "@TITLE":
                chart.Title = value;
                break;

            case "@ARTIST":
                chart.Artist = value;
                break;

            case "@DESIGN":
                chart.Designer = value;
                break;

            case "@DIFF":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var diff))
                {
                    chart.Difficulty = diff switch
                    {
                        0 => "BASIC",
                        1 => "ADVANCED",
                        2 => "EXPERT",
                        3 => "MASTER",
                        4 => "ULTIMA",
                        _ => value,
                    };
                }
                else
                {
                    chart.Difficulty = value;
                }
                break;

            case "@LEVEL":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                    chart.Level = level;
                else
                    alerts.Add(new Alert(Warning, $"@LEVEL 格式错误: {line}") { Line = lineNum });
                break;

            case "@CONST":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var constant))
                    chart.Constant = constant;
                else
                    alerts.Add(new Alert(Warning, $"@CONST 格式错误: {line}") { Line = lineNum });
                break;

            case "@SONGID":
                chart.SongId = value;
                break;

            case "@TICKS":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                    chart.TicksPerBeat = ticks;
                else
                    alerts.Add(new Alert(Warning, $"@TICKS 格式错误: {line}") { Line = lineNum });
                break;

            case "@BEAT":
                var beatParts = value.Split(' ');
                if (beatParts.Length >= 3
                    && int.TryParse(beatParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var beatMeasure)
                    && int.TryParse(beatParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var beatNum)
                    && int.TryParse(beatParts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var beatDen))
                {
                    chart.BeatEvents.Add((beatMeasure, beatNum, beatDen));
                }
                else
                {
                    alerts.Add(new Alert(Warning, $"@BEAT 格式错误: {line}") { Line = lineNum });
                }
                break;

            case "@BPM":
                var bpmPart = value;
                var bpmSpaceIdx = bpmPart.IndexOf(' ');
                if (bpmSpaceIdx > 0)
                {
                    var measureOffset = bpmPart[..bpmSpaceIdx];
                    var bpmValueStr = bpmPart[(bpmSpaceIdx + 1)..];
                    var apostropheIdx = measureOffset.IndexOf('\'');
                    if (apostropheIdx > 0
                        && int.TryParse(measureOffset[..apostropheIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bpmMeasure)
                        && int.TryParse(measureOffset[(apostropheIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bpmOffset)
                        && double.TryParse(bpmValueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpmValue))
                    {
                        chart.BpmEvents.Add((bpmMeasure, bpmOffset, bpmValue));
                    }
                    else
                    {
                        alerts.Add(new Alert(Warning, $"@BPM 格式错误: {line}") { Line = lineNum });
                    }
                }
                else
                {
                    alerts.Add(new Alert(Warning, $"@BPM 格式错误: {line}") { Line = lineNum });
                }
                break;

            default:
                alerts.Add(new Alert(Info, $"未知头部标签: {tag}") { Line = lineNum });
                break;
        }
    }

    private static int ParseNoteLine(string[] lines, int idx, UgcChart chart, List<Alert> alerts)
    {
        var line = lines[idx];
        var lineNum = idx + 1;

        var colonIdx = line.IndexOf(':');
        if (colonIdx < 0)
        {
            alerts.Add(new Alert(Warning, $"无法解析的音符行: {line}") { Line = lineNum });
            return idx;
        }

        var prefix = line[..colonIdx];
        var code = line[(colonIdx + 1)..];
        var hashIdx = prefix.IndexOf('#');
        var apostropheIdx = prefix.IndexOf('\'');
        if (hashIdx < 0 || apostropheIdx < 0 || apostropheIdx <= hashIdx + 1)
        {
            alerts.Add(new Alert(Warning, $"音符行前缀格式错误: {line}") { Line = lineNum });
            return idx;
        }

        if (!int.TryParse(prefix[(hashIdx + 1)..apostropheIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var measure))
        {
            alerts.Add(new Alert(Warning, $"无法解析 measure: {line}") { Line = lineNum });
            return idx;
        }
        if (!int.TryParse(prefix[(apostropheIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tick))
        {
            alerts.Add(new Alert(Warning, $"无法解析 tick: {line}") { Line = lineNum });
            return idx;
        }

        if (string.IsNullOrEmpty(code))
        {
            alerts.Add(new Alert(Warning, $"音符行为空: {line}") { Line = lineNum });
            return idx;
        }

        var note = new ChuNote
        {
            Measure = measure,
            Offset = tick,
        };

        var typeChar = code[0];

        switch (typeChar)
        {
            case 't':
                ParseTapNote(code, note, alerts, lineNum);
                break;

            case 'h':
                idx = ParseHoldNote(lines, idx, code, note, alerts);
                break;

            case 's':
                idx = ParseSlideNote(lines, idx, code, note, alerts);
                break;

            case 'a':
                ParseAirNote(code, note, alerts, lineNum);
                break;

            case 'x':
                ParseChrNote(code, note, alerts, lineNum);
                break;

            case 'f':
                note.Type = "FLK";
                break;

            case 'd':
                note.Type = "MNE";
                break;

            default:
                alerts.Add(new Alert(Warning, $"未知的音符类型前缀 '{typeChar}': {line}") { Line = lineNum });
                return idx;
        }

        chart.Notes.Add(note);
        return idx;
    }

    private static void ParseTapNote(string code, ChuNote note, List<Alert> alerts, int lineNum)
    {
        note.Type = "TAP";
        ParseCellWidth(code, 1, note, alerts, lineNum);
    }

    private static int ParseHoldNote(string[] lines, int idx, string code, ChuNote note, List<Alert> alerts)
    {
        note.Type = "HLD";
        ParseCellWidth(code, 1, note, alerts, idx + 1);

        if (idx + 1 < lines.Length)
        {
            var nextLine = lines[idx + 1].Trim();
            if (TryParseFollowerLine(nextLine, out var duration, out _, out _))
            {
                note.HoldDuration = duration;
                return idx + 1;
            }
        }
        alerts.Add(new Alert(Warning, $"HLD 音符缺少时长跟随行") { Line = idx + 1, RelevantNote = FormatNoteRef(note) });
        return idx;
    }

    private static int ParseSlideNote(string[] lines, int idx, string code, ChuNote note, List<Alert> alerts)
    {
        note.Type = "SLD";
        ParseCellWidth(code, 1, note, alerts, idx + 1);

        if (idx + 1 < lines.Length)
        {
            var nextLine = lines[idx + 1].Trim();
            if (TryParseFollowerLine(nextLine, out var duration, out var endCell, out var endWidth))
            {
                note.SlideDuration = duration;
                note.EndCell = endCell;
                note.EndWidth = endWidth;
                return idx + 1;
            }
        }
        alerts.Add(new Alert(Warning, $"SLD 音符缺少时长跟随行") { Line = idx + 1, RelevantNote = FormatNoteRef(note) });
        return idx;
    }

    private static bool TryParseFollowerLine(string line, out int duration, out int endCell, out int endWidth)
    {
        duration = 0;
        endCell = 0;
        endWidth = 1;

        if (!line.StartsWith('#')) return false;

        var gtSIdx = line.IndexOf(">s");
        if (gtSIdx < 1) return false;

        var durationStr = line[1..gtSIdx];
        if (!int.TryParse(durationStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out duration)) return false;

        var afterMarker = line[(gtSIdx + 2)..];
        if (afterMarker.Length >= 2)
        {
            endCell = HexCharToInt(afterMarker[0]);
            endWidth = WidthHexCharToInt(afterMarker[1]);
        }

        return true;
    }

    private static void ParseCellWidth(string code, int startIdx, ChuNote note, List<Alert> alerts, int lineNum)
    {
        if (code.Length > startIdx)
        {
            note.Cell = HexCharToInt(code[startIdx]);
            if (code.Length > startIdx + 1)
                note.Width = WidthHexCharToInt(code[startIdx + 1]);
            else
                alerts.Add(new Alert(Warning, $"音符缺少 width: {code}") { Line = lineNum, RelevantNote = FormatNoteRef(note) });
        }
        else
        {
            alerts.Add(new Alert(Warning, $"音符缺少 cell 和 width: {code}") { Line = lineNum, RelevantNote = FormatNoteRef(note) });
        }
    }

    private static void ParseAirNote(string code, ChuNote note, List<Alert> alerts, int lineNum)
    {
        var remaining = code[1..];
        var underscoreIdx = remaining.IndexOf('_');
        var mainPart = underscoreIdx >= 0 ? remaining[..underscoreIdx] : remaining;

        if (mainPart.Length < 2)
        {
            alerts.Add(new Alert(Warning, $"AIR 音符方向代码过短: {code}") { Line = lineNum });
            note.Type = "AIR";
            return;
        }

        var dir = mainPart[..2];
        if (AirDirections.TryGetValue(dir, out var airType))
        {
            note.Type = airType;
        }
        else
        {
            note.Type = "AIR";
            alerts.Add(new Alert(Warning, $"未知的 AIR 方向: {dir}") { Line = lineNum, RelevantNote = FormatNoteRef(note) });
        }

        if (mainPart.Length > 2)
        {
            note.TargetNote = mainPart[2].ToString();
        }
        else
        {
            note.TargetNote = "N";
        }

        if (underscoreIdx >= 0 && note.Type == "AHD")
        {
            var durStr = remaining[(underscoreIdx + 1)..];
            if (int.TryParse(durStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ahdDuration))
                note.AirHoldDuration = ahdDuration;
        }
    }

    private static void ParseChrNote(string code, ChuNote note, List<Alert> alerts, int lineNum)
    {
        note.Type = "CHR";
        var extra = code[1..];
        if (ChrExtras.TryGetValue(extra, out var chrDir))
            note.Extra = chrDir;
        else
            note.Extra = extra;
    }

    private static int HexCharToInt(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'F' => c - 'A' + 10,
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => 0,
        };
    }

    private static int WidthHexCharToInt(char c)
    {
        return c switch
        {
            >= '1' and <= '9' => c - '1' + 1,
            >= 'A' and <= 'G' => c - 'A' + 10,
            >= 'a' and <= 'g' => c - 'a' + 10,
            _ => 1,
        };
    }

    private static string FormatNoteRef(ChuNote note)
    {
        return $"#{note.Measure}'{note.Offset}:{note.Type}";
    }
}

