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
    
    /**
     * 新版本中二支持指定多个变速组，对音符按照分组进行变速。
     * 这里的key即为变速组id；key=0时，即为全局默认的sflList。
     */
    public Dictionary<int, List<SFL>> SpeedGroups = new()
    {
        [0] = [],
    };

    public override List<SFL> SflList
    {
        get => SpeedGroups[0];
        set => SpeedGroups[0] = value;
    }

    public override void Shift(Rational offset, decimal? bpm = null)
    {
        base.Shift(offset, bpm);
        
        bpm ??= StartBpm;
        offset = _calcOffsetForShift(offset, bpm.Value);
        foreach (var (id, sflList) in SpeedGroups)
        {
            if (id == 0) continue; // 在base.Shift里处理过了
            SpeedGroups[id] = sflList.Select(x => x with { Time = x.Time + offset })
                .Where(x => x.Time >= 0).ToList();
        }
    }
}
