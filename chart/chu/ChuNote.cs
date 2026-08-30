using MuConvert.chart;
using MuConvert.utils;
using Rationals;

namespace MuConvert.chu;

public class ChuNote: BaseNote
{
    /** 粗略的类型。可进一步结合IsAir确定具体的类型。 */
    public ChuNoteType Type { get; set; }
    public bool IsAir { get; set; } = false;
    
    /** 起始列 (0–15) */
    public int Cell { get; set; }
    /** 宽度 (1–16) */
    public int Width { get; set; } = 1;

    /**
     * 仅HOLD/SLIDE/CRUSH具有。每个segment对应ugc格式下的一个跟随行，
     * 或C2S格式下的一行（终点记载在segment对象中，起点则记载在ChuNote/上一个segment中）
     */
    public List<ChuSegment> Segments = [];

    public ExDirection? Ex { get; set; } = null;
    public bool IsEx => Ex != null || Type == ChuNoteType.Flick;
    
    /** Air音符的方向，仅对Air音符有效 */
    public AirDirection AirDirection { get; set; } = AirDirection.AIR;
    /** 起始高度。仅在Air Slide/Air Crush上具有。存储的是C2S格式中的数值，转UGC时需要调用ChuUtils中的函数做换算 */
    public decimal Height { get; set; } = 5;
    public NoteColor Color { get; set; } = NoteColor.DEF;
    /** Air Crush的interval值。null表示 $（仅在起点有combo） */
    public Rational? CrushInterval { get; set; } = null;
    
    /** 音符所在的速度组。默认情况下都为0（默认组） */
    public int SpeedGroup { get; set; } = 0;
    
    public Rational Duration => Segments.Select(x=>x.Length).Sum().CanonicalForm;
    public override Rational EndTime => (Time + Duration).CanonicalForm;
    
    /**
     * 仅Air和Air Slide适用，记录它所依附的音符（Tap/Flick/Hold/Slide等）。C2S中需要用到。
     * 不难分析出，在完成整个chart之后，这个属性其实可以根据完整chart的列表动态推断的。
     * 因此，在BaseChuParser类中提供了FillAllPrevious方法，该方法应该在所有Note被正常解析完成后调用，填充所有上述类型的音符的targetNote信息。这样就不用每个Parser都写一段相似的逻辑。
     */
    public ChuNote? TargetNote;
}

public class ChuSegment(ChuNote note)
{
    public bool C;
    public Rational Length;
    public int EndCell = note.Cell;
    public int EndWidth = note.Width;
    public decimal EndHeight = note.Height;
    
    public ChuNote Note => note;
}

public enum ChuNoteType
{
    Tap, // 单点的音符。包括TAP、CHR、AIR系列等，都属于这个
    Flick, 
    Hold, // 包括Hold和Air-Hold
    Slide, // 包括Slide和Air-Slide
    Crush, // Air-Crush
    Mine, // 地雷
}

public enum ExDirection
{
    UP, DW, CE, LS, RS, RC, LC, BS
}

public enum AirDirection
{
    AIR, AUR, AUL, ADW, ADR, ADL,
}

public enum NoteColor
{
    DEF, // Normal / 通常
    NON, // Transparent / 透明
    RED, // Red / 赤
    ORN, // Orange / 橙
    YEL, // Yellow / 黄
    LIM, // Grass / 黄緑
    GRN, // Green / 緑
    AQA, // Sky / 水
    CYN, // Sky blue / 空
    DGR, // （存疑，不确定） Cobalt blue / 天（DGR ≈ Dark GRay？）
    BLU, // Blue / 青
    VLT, // Violet / 青紫
    PPL, // Purple / 赤紫
    PNK, // Pink / 桃
    GRY, // （有点存疑，略微不确定） White / 白
    BLK, // Black / 黒
}