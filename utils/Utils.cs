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

    public static (int, int) BarAndTick(Rational time, int resolution, int extraTicks = 0)
    {
        var bar = time.WholePart;
        var tick = (time.FractionPart * resolution).Round();
        tick += extraTicks;
        
        while (tick >= resolution)
        {
            tick -= resolution;
            bar++;
        }
        while (tick < 0)
        {
            tick += resolution;
            bar--;
        }

        return ((int)bar, (int)tick);
    }
    
    public static int Tick(Rational time, int resolution, int extraTicks = 0, int? min = null)
    {
        var r = (int)((time * resolution).Round() + extraTicks);
        if (min != null && r < min) r = min.Value;
        return r;
    }
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
    
    private static readonly Rational _half = new(1, 2);
    // 工作范围仅限非负数；舍入策略方面，使用与系统库Math.Round相同的“四舍六入五成双”算法。
    public static BigInteger Round(this Rational r)
    {
        if (r < 0) throw new ArgumentOutOfRangeException(nameof(r));
        var whole = r.WholePart;
        var frac = r.FractionPart;
        var shouldAdd = frac > _half || (frac == _half && whole % 2 == 1);
        return whole + (shouldAdd ? 1 : 0);
    }
    
    public static Rational Sum(this IEnumerable<Rational> source)
    {
        return source.Aggregate(Rational.Zero, (acc, r) => acc + r);
    }
    
    internal static Dictionary<K, V> RemoveRange<K, V>(this Dictionary<K, V> dict, IEnumerable<K> keys) where K : notnull
    {
        foreach (var key in keys) dict.Remove(key);
        return dict;
    }
    
    internal static Dictionary<K, V> Concat<K, V>(this Dictionary<K, V> dict, Dictionary<K, V> dict2) where K : notnull
    {
        foreach (var (k, v) in dict2) dict[k] = v;
        return dict;
    }

    internal static void SetFirst<T>(this List<T> list, T? item)
    {
        if (item != null)
        {
            if (list.Count == 0) list.Add(item);
            else list[0] = item;
        }
        else
        {
            if (list.Count == 1) list.RemoveAt(0);
            else if (list.Count > 1) list[0] = item!; // item是null，所以直接赋值进去就是null了
        }
    }
}
