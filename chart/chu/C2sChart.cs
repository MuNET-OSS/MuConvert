using MuConvert.chart;

namespace MuConvert.chu;

/**
 * C2S 格式谱面 IR（官方格式，RESOLUTION=384 tick/小节）。
 */
public class C2sChart : BaseChart<ChuNote>, IChuChart
{
    public string Version { get; set; } = "1.08.00\t1.08.00";
    public int MusicId { get; set; }
    public int DifficultId { get; set; }
    public string Creator { get; set; } = "";
    public int Resolution { get; set; } = 384;
    public double DefBpm { get; set; } = 120.0;
    public List<(int Measure, int Offset, double Bpm)> BpmEvents = [];
    public List<(int Measure, int Offset, int Denom, int Num)> MetEvents = [];
    public List<(int Measure, int Offset, int Duration, double Multiplier)> SflEvents = [];

    public override decimal StartBpm => (decimal)(BpmEvents.Count > 0 ? BpmEvents[0].Bpm : DefBpm);
    public override decimal StartTime => Notes.Count > 0 ? Notes.Min(n => n.Measure * Resolution + n.Offset) / (decimal)Resolution * 240m / StartBpm : 0;
    public override decimal EndTime => Notes.Count > 0 ? Notes.Max(n => n.Measure * Resolution + n.Offset + Math.Max(n.HoldDuration, Math.Max(n.SlideDuration, n.AirHoldDuration))) / (decimal)Resolution * 240m / StartBpm : 0;
    public override int TotalNotes => Notes.Count;
}
