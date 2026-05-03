using MuConvert.chart;
using Rationals;

namespace MuConvert.chu;

public class ChuChart : BaseChart<ChuNote>
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Designer { get; set; } = ""; // 谱师
    public int Difficulty { get; set; } = 3; // 难度，0-basic, 1-advanced, ...。大多数情况下都是数字字符串。不直接存成数字是为了，万一自制谱这里写的不是数字、保留鲁棒性
    public string DisplayLevel { get; set; } = ""; // 显示等级，字符串
    public decimal Level { get; set; } // 定数，小数
    public string MusicId { get; set; } = "0";
    public List<(Rational Time, Rational Duration, decimal Multiplier)> SflList = []; // 所有变速声明构成的列表。
}
