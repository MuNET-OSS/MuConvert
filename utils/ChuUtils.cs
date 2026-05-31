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
    };
    public static readonly Dictionary<string, string> C2U_ChrExtras = Utils.ReverseDict(U2C_ChrExtras);
    
    public static readonly Dictionary<string, string> U2C_AirColor = new()
    {
        ["N"] = "DEF",
        ["I"] = "I", // TODO 搞清楚UGC里的'I'颜色，在C2S里，对应的字符串是什么
    };
    public static readonly Dictionary<string, string> C2U_AirColor = Utils.ReverseDict(U2C_AirColor);

    public static decimal U2C_Height(decimal input) => input / 1.6m;
    public static decimal C2U_Height(decimal input) => input * 1.6m;
    
    public static bool IsHold(string t) => t is "HLD" or "HXD";
    public static bool IsSlide(string t) => t is "SLD" or "SLC" or "SXD" or "SXC";
    public static bool IsAirSlide(string t) => t is "ASD" or "ASC";
    public static bool IsAir(string t) => t is "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL";
    public static bool IsAirHold(string t) => t is "AHD" or "AHX";
    public static bool IsAirCrush(string t) => t is "ALD";
    // 是否是广义的air音符（Air/Air Hold/Air Slide/Air Crush）
    public static bool IsGeneralizedAir(string t) => IsAir(t) || IsAirHold(t) || IsAirSlide(t) || IsAirCrush(t);
    
    public static bool IsHold(ChuNote? n) => IsHold(n?.Type!);
    public static bool IsSlide(ChuNote? n) => IsSlide(n?.Type!);
    public static bool IsAirSlide(ChuNote? n) => IsAirSlide(n?.Type!);
    public static bool IsAir(ChuNote? n) => IsAir(n?.Type!);
    public static bool IsAirHold(ChuNote? n) => IsAirHold(n?.Type!);
    public static bool IsAirCrush(ChuNote? n) => IsAirCrush(n?.Type!);
    // 是否是广义的air音符（Air/Air Hold/Air Slide/Air Crush）
    public static bool IsGeneralizedAir(ChuNote? n) => IsGeneralizedAir(n?.Type!);

    public static bool TryH36ToI(string str, out int result) => Utils.TryHToI(str, 36, out result);
    public static string IToH36(int value) => Utils.IToH(value, 36);
}