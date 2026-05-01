using MuConvert.chart;

namespace MuConvert.chu;

/**
 * SUS 格式谱面 IR（REQUEST=480 tick/拍，lane 0–31）。
 */
public class SusChart : BaseChart<ChuNote>, IChuChart
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Designer { get; set; } = "";
    public int TicksPerBeat { get; set; } = 480;
    public double Bpm { get; set; } = 120.0;

    public override decimal StartBpm => (decimal)Bpm;
    public override decimal StartTime => Notes.Count > 0 ? Notes.Min(n => n.Measure * TicksPerBeat * 4 + n.Offset) / (decimal)(TicksPerBeat * 4) * 240m / StartBpm : 0;
    public override decimal EndTime => Notes.Count > 0 && StartBpm > 0 ? Notes.Max(n => n.Measure * TicksPerBeat * 4 + n.Offset + Math.Max(n.HoldDuration, Math.Max(n.SlideDuration, n.AirHoldDuration))) / (decimal)(TicksPerBeat * 4) * 240m / StartBpm : 0;
    public override int TotalNotes => Notes.Count;
}
