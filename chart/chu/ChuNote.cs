using MuConvert.chart;
using Rationals;

namespace MuConvert.chu;

/**
 * CHUNITHM 通用音符，C2S / UGC / SUS 共用此结构。
 */
public class ChuNote: BaseNote
{
    /** 音符类型 (TAP, CHR, HLD, SLD, AIR, AHD 等) */
    public string Type { get; set; } = "TAP";
    /** 起始列 (0–15) */
    public int Cell { get; set; }
    /** 宽度 (1–16) */
    public int Width { get; set; } = 1;
    /** HLD/SLD/AHD/ASD等的 持续时长 */
    public Rational Duration { get; set; } = 0;
    /** SLD 终点列 */
    public int EndCell { get; set; }
    /** SLD 终点宽度 */
    public int EndWidth { get; set; } = 1;
    /** Air系列音符/Slide系列音符的 关联的目标音符类型 */
    public string TargetNote { get; set; } = "";
    /** CHR/FLK/Air系列音符可能会具有的标记（如UP、L、DEF等） */
    public string Tag { get; set; } = "";
    /** ASD/ASC/ALD上具有的、目前含义还不明确的字段，统一收集到这个里面。 */
    public List<int> ExtraData = [];
    
    public override Rational EndTime => Time + Duration;
}
