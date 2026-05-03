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
    public List<(int Measure, int Offset, double Bpm)> BpmEvents = [];
}
