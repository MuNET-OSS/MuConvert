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
}
