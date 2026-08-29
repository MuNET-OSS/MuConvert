using MuConvert.chart;
using MuConvert.utils;
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
    public Rational Duration { get; set => field = value.CanonicalForm; } = 0;

    /** SLD 终点列 */
    public int EndCell
    {
        get => _endCell ?? Cell;
        set => _endCell = value;
    }
    /** SLD 终点宽度 */
    public int EndWidth
    {
        get => _endWidth ?? Width;
        set => _endWidth = value;
    }
    
    /**
     * 当前音符的”前驱“。对不同类型的音符，其定义不同：
     * - 对 Slide(SLD/SLC)，是该slide对应的前一段slide。（对首段slide，该值为null）
     * - 对 Air(AIR/AUR/AUL/ADW/ADR/ADL)，是它所依附的音符（可以是tap\hold等任何类型,应该只要不是air系列和aircrush(ALD)都行）
     * - 对 Air Slide(ASD/ASC)：对首段slide，同Air的情况、是它所依附的音符；对第二段及之后的slide，同Slide的情况，是该slide对应的前一段slide。
     *
     * 不难分析出，在完成整个chart之后，这个属性其实可以根据完整chart的列表动态推断的。
     * 因此，在BaseChuParser类中提供了FillAllPrevious方法，该方法应该在所有Note被正常解析完成后调用，填充所有上述类型的音符的targetNote信息。这样就不用每个Parser都写一段相似的逻辑。
     */
    public ChuNote? Previous;
    
    /** CHR/FLK/Air系列音符可能会具有的标记（如UP、L、DEF等） */
    public string Tag { get; set; } = "";
    
    /** 起始高度。仅在Air Slide/Air Crush上具有。存储的是C2S格式中的数值，转UGC时需要乘以1.6。 */
    public decimal Height { get; set; } = 5;
    /** 结束高度。仅在Air Slide/Air Crush上具有。存储的是C2S格式中的数值，转UGC时需要乘以1.6。 */
    public decimal EndHeight { get; set; } = 5;
    /** Air Crush的interval值 */
    public Rational CrushInterval { get; set; } = 0;
    
    public override Rational EndTime => (Time + Duration).CanonicalForm;
    /** Air系列音符/Slide系列音符的 关联的目标音符类型。仅供向前兼容使用。 */
    public string TargetNote => ChuUtils.AsTargetType(Previous) ?? "N";

    private int? _endCell;
    private int? _endWidth;
}
