using MuConvert.chart;

namespace MuConvert.chu;

/**
 * UGC 格式谱面 IR（UMIGURI 格式，@TICKS=480 tick/拍）。
 */
public class UgcChart : BaseChart<ChuNote>, IChuChart
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Designer { get; set; } = "";
    public int Difficulty { get; set; } = 3;
    public string DisplayLevel = "";
    public decimal Level { get; set; }
    public string MusicId { get; set; } = "";
    public int TicksPerBeat { get; set; } = 480;
    public List<(int Measure, int Num, int Den)> BeatEvents = [];
    public List<(int Measure, int Offset, double Bpm)> BpmEvents = [];
    public List<(int Measure, int Offset, double Multiplier)> SpeedEvents = [];
}
