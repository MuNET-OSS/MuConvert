using System.Globalization;
using System.Numerics;
using System.Reflection;
using Rationals;

namespace MuConvert.utils;

public static class Utils
{
    internal static void Assert(bool condition, string msg = "")
    {
        if (!condition) throw new Exception(string.Format(Locale.AssertionFailed, msg));
    }
    
    internal static Exception Fail(string msg = "")
    {
        return new Exception(string.Format(Locale.AssertionFailed, msg));
    }
    
    public static string AppVersion => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion[..^33] ?? "unknown";

    public static void SetLocale(CultureInfo culture) => Locale.Culture = culture;

    internal static void Add<K, V>(this Dictionary<K, List<V>> dict, K key, V value) where K : notnull
    {
        if (!dict.ContainsKey(key)) dict[key] = new();
        dict[key].Add(value);
    }
    
    public static BigInteger LCM(BigInteger a, BigInteger b) => a / BigInteger.GreatestCommonDivisor(a, b) * b;

    public static BigInteger LCM(IEnumerable<BigInteger> values) => values.Aggregate(LCM);

    // 工作范围仅限正数
    public static Rational Ceil(Rational r) => r.WholePart + (r.FractionPart == 0 ? 0 : 1);
    
    public static BigInteger Max(BigInteger a, BigInteger b) => a > b ? a : b;
}