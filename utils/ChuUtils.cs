using MuConvert.chu;

namespace MuConvert.utils;

public class ChuUtils
{
    public static readonly Dictionary<AirDirection, string> AirDirections_ToUgc = new()
    {
        [AirDirection.AIR] = "UC",
        [AirDirection.AUR] = "UR",
        [AirDirection.AUL] = "UL",
        [AirDirection.ADW] = "DC",
        [AirDirection.ADR] = "DR",
        [AirDirection.ADL] = "DL",
    };
    public static readonly Dictionary<string, AirDirection> AirDirections_FromUgc = Utils.ReverseDict(AirDirections_ToUgc);

    public static readonly Dictionary<ExDirection, string> ExDirections_ToUgc = new()
    {
        [ExDirection.UP] = "U",
        [ExDirection.DW] = "D",
        [ExDirection.CE] = "C",
        [ExDirection.LS] = "L",
        [ExDirection.RS] = "R",
        [ExDirection.RC] = "A",
        [ExDirection.LC] = "W",
        [ExDirection.BS] = "I",
    };
    public static readonly Dictionary<string, ExDirection> ExDirections_FromUgc = Utils.ReverseDict(ExDirections_ToUgc);
    
    public static readonly Dictionary<NoteColor, string> AirCrush_Color_ToUgc = new()
    {
        [NoteColor.DEF] = "0", // Normal / 通常
        [NoteColor.NON] = "Z", // Transparent / 透明
        [NoteColor.RED] = "1", // Red / 赤
        [NoteColor.ORN] = "2", // Orange / 橙
        [NoteColor.YEL] = "3", // Yellow / 黄
        [NoteColor.LIM] = "4", // Grass / 黄緑
        [NoteColor.GRN] = "5", // Green / 緑
        [NoteColor.AQA] = "6", // Sky / 水
        [NoteColor.CYN] = "7", // Sky blue / 空
        [NoteColor.DGR] = "8", // （存疑，不确定） Cobalt blue / 天（DGR ≈ Dark GRay？）
        [NoteColor.BLU] = "9", // Blue / 青
        [NoteColor.VLT] = "A", // Violet / 青紫
        [NoteColor.PPL] = "Y", // Purple / 赤紫
        [NoteColor.PNK] = "B", // Pink / 桃
        [NoteColor.GRY] = "C", // （有点存疑，略微不确定） White / 白
        [NoteColor.BLK] = "D", // Black / 黒
    };
    public static readonly Dictionary<string, NoteColor> AirCrush_Color_FromUgc = Utils.ReverseDict(AirCrush_Color_ToUgc);
    
    public static string AirColor_ToUgc(ChuNote n)
    {
        if (n.Type == ChuNoteType.Crush) return AirCrush_Color_ToUgc[n.Color];
        else if (IsAirDown(n))
        {
            if (n.Color == NoteColor.GRN) return "I";
        }
        else if (n.Color == NoteColor.PPL) return "I";
        return "N";
    }
    public static bool AirColor_FromUgc(ChuNote n, string rawColorStr, out NoteColor color)
    {
        if (n.Type == ChuNoteType.Crush) return AirCrush_Color_FromUgc.TryGetValue(rawColorStr, out color);
        
        color = NoteColor.DEF;
        if (rawColorStr == "N") return true;
        else if (rawColorStr == "I")
        {
            color = IsAirDown(n) ? NoteColor.GRN : NoteColor.PPL;
            return true;
        }
        return false;
    }
    
    public static decimal Height_ToUgc(decimal input) => (input - 1) * 2;
    public static decimal Height_FromUgc(decimal input) => input / 2 + 1;
    
    public static bool IsAir(ChuNote? n) => n is { IsAir: true, Type: ChuNoteType.Tap };
    public static bool IsAirSlide(ChuNote? n) => n is { IsAir: true, Type: ChuNoteType.Slide };
    public static bool IsAirHold(ChuNote? n) => n is { IsAir: true, Type: ChuNoteType.Hold };
    public static bool IsAirDown(ChuNote? n) => IsAir(n) && n!.AirDirection >= AirDirection.ADW;
    
    public static bool TryH36ToI(string str, out int result) => Utils.TryHToI(str, 36, out result);
    public static string IToH36(int value) => Utils.IToH(value, 36);

    public static string? AsC2sPreviousStr(ChuNote? n) => n?.Type switch
    {
        ChuNoteType.Tap when !n.IsAir => n.IsEx ? "CHR" : "TAP",
        ChuNoteType.Flick => "FLK",
        ChuNoteType.Mine => "MNE",
        ChuNoteType.Hold => n.IsAir ? "AHD" : "HLD",
        ChuNoteType.Slide => n.IsAir ? 
            (n.Segments.LastOrDefault()?.C == true ? "ASC" : "ASD") : 
            "SLD",
        _ => null,
    };

    public static bool NeedsTargetNote(ChuNote n)
    {
        return IsAir(n) || IsAirHold(n) || IsAirSlide(n);
    }
}