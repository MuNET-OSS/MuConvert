using MuConvert.chart;
using Rationals;

namespace MuConvert.chu;

public class ChuChart : BaseChart<ChuNote>
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Designer { get; set; } = ""; // 谱师
    public int Difficulty { get; set; } = 3; // 难度，0-basic, 1-advanced, ...。
    public string DisplayLevel { get; set; } = ""; // 显示等级，字符串
    public decimal Level { get; set; } // 定数，小数
    public string MusicId { get; set; } = "0";
}
