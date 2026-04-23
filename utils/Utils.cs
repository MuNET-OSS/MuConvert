using System.Globalization;
using System.Numerics;
using System.Reflection;
using Rationals;
using L = MuConvert.Antlr.SimaiLexer;

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

    public static BigInteger LCM(BigInteger a, BigInteger b) => a / BigInteger.GreatestCommonDivisor(a, b) * b;

    public static BigInteger LCM(IEnumerable<BigInteger> values) => values.Aggregate(LCM);
    
    public static BigInteger Max(BigInteger a, BigInteger b) => a > b ? a : b;
    
    public static Rational Min(Rational a, Rational b) => a < b ? a : b;
    
    private static readonly Dictionary<string, int> _simaiLexerMap = Enumerable.Range(1, L.ruleNames.Length)
        .Where(i=>L.DefaultVocabulary.GetLiteralName(i) != null)
        .ToDictionary(i => L.DefaultVocabulary.GetLiteralName(i)[1..^1], i => i);

    internal static int TokenType(string str) => _simaiLexerMap[str];
    
    internal static bool IsModifier(int tokenType) => tokenType is L.MODIFIER or L.TAP_TO_STAR or L.STAR_TO_TAP or L.NO_STAR;
}

internal static class ExtensionUtils
{
    internal static void Add<K, V>(this Dictionary<K, List<V>> dict, K key, V value) where K : notnull
    {
        if (!dict.ContainsKey(key)) dict[key] = [];
        dict[key].Add(value);
    }

    internal static Dictionary<K, V> EnsureKeys<K, V>(
        this Dictionary<K, V> dict,
        IEnumerable<K> requiredKeys,
        V defaultValue = default!) where K : notnull
    {
        foreach (var key in requiredKeys) dict.TryAdd(key, defaultValue);
        return dict;
    }
    
    // 工作范围仅限正数
    public static Rational Ceil(this Rational r)
    {
        if (r < 0) throw new ArgumentOutOfRangeException(nameof(r));
        return r.WholePart + (r.FractionPart == 0 ? 0 : 1);
    }
}
