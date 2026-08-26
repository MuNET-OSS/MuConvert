using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
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
    
    public static Rational Max(Rational a, Rational b) => a > b ? a : b;
    
    public static Rational Min(Rational a, Rational b) => a < b ? a : b;
    
    private static readonly Dictionary<string, int> _simaiLexerMap = Enumerable.Range(1, L.ruleNames.Length)
        .Where(i=>L.DefaultVocabulary.GetLiteralName(i) != null)
        .ToDictionary(i => L.DefaultVocabulary.GetLiteralName(i)[1..^1], i => i);

    internal static int TokenType(string str) => _simaiLexerMap[str];
    
    internal static bool IsModifier(int tokenType) => tokenType is L.MODIFIER or L.TAP_TO_STAR or L.STAR_TO_TAP or L.NO_STAR;
    
    internal static bool IsBoundaryToken(int type) => type is L.COMMA or L.FALSE_EACH or L.CHART_END || type == TokenType("/");

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
    
    /**
     * 把N进制的一位数转为int。(返回bool成功与否，通过out变量传递结果)
     * 可以是从11到36进制数，（如常见的16进制数或UGC的36进制数），都是能用的。因为本质都是10以后按照ABCDE...的顺序排列，所以没区别。
     */
    public static bool TryHToI(char _char, out int result)
    {
        result = _char switch
        {
            >= '0' and <= '9' => _char - '0',
            >= 'a' and <= 'z' => _char - 'a' + 10,
            >= 'A' and <= 'Z' => _char - 'A' + 10,
            _ => -1
        };
        return _char >= 0;
    }
    
    /**
     * 把N进制的一位数转为int。（直接返回int，如果转换失败抛异常）
     * 可以是从11到36进制数，（如常见的16进制数或UGC的36进制数），都是能用的。因为本质都是10以后按照ABCDE...的顺序排列，所以没区别。
     */
    public static int HToI(char _char) =>
        TryHToI(_char, out var i) ? i : throw new FormatException($"Cannot convert '{_char}' to int!");
    
    /**
     * 把N进制的多位数转为int。(返回bool成功与否，通过out变量传递结果)
     * N进制的N值需要传入，需要满足 N在[11,36]之间。
     */
    public static bool TryHToI(string str, int N, out int result)
    {
        result = 0;
        foreach (var ch in str)
        {
            if (!TryHToI(ch, out var bit)) return false;
            result = result * N + bit;
        }
        return true;
    }
    
    /**
     * 把N进制的多位数转为int。（直接返回int，如果转换失败抛异常）
     * N进制的N值需要传入，需要满足 N在[11,36]之间。
     */
    public static int HToI(string str, int N) =>
        TryHToI(str, N, out var i) ? i : throw new FormatException($"Cannot convert '{str}' to int!");

    /**
     * 把int转为N进制的字符串。（直接返回int，如果转换失败抛异常）
     * N进制的N值需要传入，需要满足 N在[11,36]之间。
     */
    public static string IToH(int value, int N)
    {
        if (N is < 11 or > 36) throw new ArgumentOutOfRangeException(nameof(N), N, "N must be in [11, 36].");
        if (value == 0) return "0";

        var negative = value < 0;
        value = Math.Abs(value);
        var sb = new StringBuilder();
        while (value > 0)
        {
            var d = value % N;
            value /= N;
            // 生成的时候先倒着生成字符数组，返回的时候统一reverse
            sb.Append(d < 10 ? (char)('0' + d) : (char)('A' + (d - 10)));
        }
        if (negative) sb.Append('-');
        return new string(sb.ToString().Reverse().ToArray());
    }
    
    public static Dictionary<string, string> ReverseDict(Dictionary<string, string> dict) =>
        dict.ToDictionary(x => x.Value, x => x.Key);
}

public static class ExtensionUtils
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
    public static BigInteger Ceil(this Rational r)
    {
        if (r < 0) throw new ArgumentOutOfRangeException(nameof(r));
        return r.WholePart + (r.FractionPart == 0 ? 0 : 1);
    }
    
    private static readonly Rational _half = new(1, 2);
    // 舍入策略方面，使用与系统库Math.Round相同的“四舍六入五成双”算法。
    public static BigInteger Round(this Rational r)
    {
        var whole = r.WholePart;
        var frac = r.FractionPart;
        var shouldAdd = frac > _half || (frac == _half && !whole.IsEven);
        return whole + (shouldAdd ? 1 : 0);
    }
    
    public static Rational Sum(this IEnumerable<Rational> source)
    {
        return source.Aggregate(Rational.Zero, (acc, r) => acc + r);
    }
    
    public static Rational Abs(this Rational r)
    {
        return r * r.Sign;
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
