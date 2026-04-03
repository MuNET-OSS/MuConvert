using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace MuConvert.utils;

public static class Utils
{
    public static void Assert(bool condition, string msg = "")
    {
        if (!condition) throw new Exception(string.Format(Locale.AssertionFailed, msg));
    }
    
    public static Exception Fail(string msg = "")
    {
        return new Exception(string.Format(Locale.AssertionFailed, msg));
    }
    
    public static string AppVersion => FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion?[..^33] ?? "unknown";

    public static void SetLocale(CultureInfo culture) => Locale.Culture = culture;

    public static void Add<K, V>(this Dictionary<K, List<V>> dict, K key, V value)
    {
        if (!dict.ContainsKey(key)) dict[key] = new();
        dict[key].Add(value);
    }
}