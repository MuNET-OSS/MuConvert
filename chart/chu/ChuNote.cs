namespace MuConvert.chu;

/**
 * CHUNITHM 通用音符，C2S / UGC / SUS 共用此结构。
 */
public class ChuNote
{
    /** 音符类型 (TAP, CHR, HLD, SLD, AIR, AHD 等) */
    public string Type { get; set; } = "TAP";
    /** 小节号 */
    public int Measure { get; set; }
    /** 小节内偏移 (C2S: 0–383, UGC/SUS: 0–1919) */
    public int Offset { get; set; }
    /** 起始列 (0–15) */
    public int Cell { get; set; }
    /** 宽度 (1–16) */
    public int Width { get; set; } = 1;
    /** HLD 持续时长 */
    public int HoldDuration { get; set; }
    /** SLD 持续时长 */
    public int SlideDuration { get; set; }
    /** SLD 终点列 */
    public int EndCell { get; set; }
    /** SLD 终点宽度 */
    public int EndWidth { get; set; } = 1;
    /** CHR/FLK 附加数据（方向等） */
    public string Tag { get; set; } = "";
    /** AIR/AHD 关联的目标音符类型 */
    public string TargetNote { get; set; } = "";
    /** AHD 持续时长 */
    public int AirHoldDuration { get; set; }
    /** Air Crush 起始高度 */
    public int StartHeight { get; set; }
    /** Air Crush 目标高度 */
    public int TargetHeight { get; set; }
    /** Air Crush 颜色 */
    public string NoteColor { get; set; } = "";
}
