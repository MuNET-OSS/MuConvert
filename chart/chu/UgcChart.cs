using MuConvert.chart;
using Rationals;

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
    public List<(Rational Time, Rational Duration, decimal Multiplier)> SflList = []; // 所有变速声明构成的列表。
}
