using MuConvert.chu;

namespace MuConvert.utils;

public class ChuUtils
{
    public static readonly Dictionary<string, string> U2C_AirDirections = new()
    {
        ["UC"] = "AIR",
        ["UR"] = "AUR",
        ["UL"] = "AUL",
        ["DC"] = "ADW",
        ["DR"] = "ADR",
        ["DL"] = "ADL",
    };
    public static readonly Dictionary<string, string> C2U_AirDirections = Utils.ReverseDict(U2C_AirDirections);

    public static readonly Dictionary<string, string> U2C_ChrExtras = new()
    {
        ["U"] = "UP",
        ["D"] = "DW",
        ["C"] = "CE",
        ["L"] = "LS",
        ["R"] = "RS",
        ["A"] = "RC",
        ["W"] = "LC",
        ["I"] = "BS",
    };
    public static readonly Dictionary<string, string> C2U_ChrExtras = Utils.ReverseDict(U2C_ChrExtras);
    
    public static bool Try_U2C_AirColor(ChuNote n, string rawColorStr, out string color)
    {
        color = rawColorStr switch
        {
            "N" => "DEF",
            "I" => IsAirDown(n) ? "GRN" : "PPL",
            _ => "",
        };
        return color != "";
    }
    public static bool Try_C2U_AirColor(ChuNote n, out string color)
    {
        color = n.Tag switch
        {
            "DEF" => "N",
            "" => "N",
            "GRN" => IsAirDown(n) ? "I" : "N",
            "PPL" => IsAirDown(n) ? "N" : "I",
            _ => "",
        };
        return color != "";
    }
    public static readonly HashSet<string> C2sAllowedColors = ["DEF", "GRN", "PPL"];
    
    public static readonly Dictionary<string, string> U2C_AirCrushColor = new()
    {
        ["0"] = "DEF", // Normal / 通常
        ["Z"] = "NON", // Transparent / 透明
        ["1"] = "RED", // Red / 赤
        ["2"] = "ORN", // Orange / 橙
        ["3"] = "YEL", // Yellow / 黄
        ["4"] = "LIM", // Grass / 黄緑
        ["5"] = "GRN", // Green / 緑
        ["6"] = "AQA", // Sky / 水
        ["7"] = "CYN", // Sky blue / 空
        ["8"] = "DGR", // （存疑，不确定） Cobalt blue / 天（DGR ≈ Dark GRay？）
        ["9"] = "BLU", // Blue / 青
        ["A"] = "VLT", // Violet / 青紫
        ["Y"] = "PPL", // Purple / 赤紫
        ["B"] = "PNK", // Pink / 桃
        ["C"] = "GRY", // （有点存疑，略微不确定） White / 白
        ["D"] = "BLK", // Black / 黒
    };
    public static readonly Dictionary<string, string> C2U_AirCrushColor = Utils.ReverseDict(U2C_AirCrushColor);
    public static readonly HashSet<string> C2sAllowedCrushColors = C2U_AirCrushColor.Keys.ToHashSet();
    
    public static decimal U2C_Height(decimal input) => input / 2 + 1;
    public static decimal C2U_Height(decimal input) => (input - 1) * 2;
    
    public static bool IsHold(string t) => t is "HLD" or "HXD";
    public static bool IsSlide(string t) => t is "SLD" or "SLC" or "SXD" or "SXC";
    public static bool IsAirSlide(string t) => t is "ASD" or "ASC";
    public static bool IsAir(string t) => t is "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL";
    public static bool IsAirDown(string t) => t is "ADW" or "ADR" or "ADL";
    public static bool IsAirHold(string t) => t is "AHD" or "AHX";
    public static bool IsAirCrush(string t) => t is "ALD";
    // 是否是广义的air音符（Air/Air Hold/Air Slide/Air Crush）
    public static bool IsGeneralizedAir(string t) => IsAir(t) || IsAirHold(t) || IsAirSlide(t) || IsAirCrush(t);
    
    public static bool IsHold(ChuNote? n) => IsHold(n?.Type!);
    public static bool IsSlide(ChuNote? n) => IsSlide(n?.Type!);
    public static bool IsAirSlide(ChuNote? n) => IsAirSlide(n?.Type!);
    public static bool IsAir(ChuNote? n) => IsAir(n?.Type!);
    public static bool IsAirDown(ChuNote? n) => IsAirDown(n?.Type!);
    public static bool IsAirHold(ChuNote? n) => IsAirHold(n?.Type!);
    public static bool IsAirCrush(ChuNote? n) => IsAirCrush(n?.Type!);
    // 是否是广义的air音符（Air/Air Hold/Air Slide/Air Crush）
    public static bool IsGeneralizedAir(ChuNote? n) => IsGeneralizedAir(n?.Type!);
    
    public static bool IsSlideChainNote(string t) => IsSlide(t) || IsAirSlide(t) || IsAirHold(t) || IsAirCrush(t);
    public static bool IsChainContinueSegments(ChuNote n) // 返回 true 表示当前 segment 接在同类型链的上一段之后，而非首段。
        => (IsSlide(n) && IsSlide(n.Previous))
           || (IsAirSlide(n) && IsAirSlide(n.Previous))
           || (IsAirHold(n) && IsAirHold(n.Previous))
           || (IsAirCrush(n) && IsAirCrush(n.Previous));

    public static bool TryH36ToI(string str, out int result) => Utils.TryHToI(str, 36, out result);
    public static string IToH36(int value) => Utils.IToH(value, 36);

    public static string? AsTargetType(ChuNote? n) => n?.Type switch
    {
        null => null,
        "HXD" => "HLD",
        "SLC" or "SXD" or "SXC" => "SLD",
        "AHX" => "AHD",
        _ => n.Type
    };
}