using MuConvert.chart;

namespace MuConvert.chu;

/**
 * UGC 格式谱面 IR（UMIGURI 格式，@TICKS=480 tick/拍）。
 */
public class UgcChart : BaseChart<ChuNote>, IChuChart
{
    public string Version { get; set; } = "6";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Designer { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public int Level { get; set; }
    public double Constant { get; set; }
    public string SongId { get; set; } = "";
    public int TicksPerBeat { get; set; } = 480;
    public List<(int Measure, int Num, int Den)> BeatEvents = [];
    public List<(int Measure, int Offset, double Bpm)> BpmEvents = [];
    public List<(int Measure, int Offset, double Multiplier)> SpeedEvents = [];

    public override decimal StartBpm => (decimal)(BpmEvents.Count > 0 ? BpmEvents[0].Bpm : 120.0);
    public override decimal StartTime => Notes.Count > 0 ? Notes.Min(n => n.Measure * TicksPerBeat * 4 + n.Offset) / (decimal)(TicksPerBeat * 4) * 240m / StartBpm : 0;
    public override decimal EndTime => Notes.Count > 0 && StartBpm > 0 ? Notes.Max(n => n.Measure * TicksPerBeat * 4 + n.Offset + Math.Max(n.HoldDuration, Math.Max(n.SlideDuration, n.AirHoldDuration))) / (decimal)(TicksPerBeat * 4) * 240m / StartBpm : 0;
    public override int TotalNotes => Notes.Count;
}
