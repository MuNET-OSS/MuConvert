using System.CommandLine;
using System.Text;
using MuConvert.generator;
using MuConvert.maidata;
using MuConvert.parser.simai;
using MuConvert.utils;

namespace MuConvert;

internal static class Program
{
    private static int Main(string[] args)
    {
        var root = BuildRootCommand();
        try
        {
            return root.Parse(args).Invoke();
        }
        catch (ConversionException ex)
        {
            PrintAlerts(ex.Alerts, "转换失败：");
            Console.Error.WriteLine("转换失败！报错详见如上。您可以通过 https://github.com/MuNet-OSS/MuConvert/issues 反馈问题。");
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Command BuildRootCommand()
    {
        var root = new RootCommand
        {
            Description = $"MuConvert {Utils.AppVersion} — simai / maidata → MA2\n" + 
                          "将 .txt 格式的 simai 单谱或 maidata 转为 MA2，输出与输入同目录的 lv_N.ma2。"
        };

        var levelsOption = new Option<string?>("--levels", "-l")
        {
            Description = "仅转换指定难度（maidata 的 inote 编号），逗号分隔；省略则全部。纯 simai 单谱不可使用本选项。",
            HelpName = "N[,N...]"
        };

        var inputArgument = new Argument<string>("inputfile")
        {
            Description = "输入 .txt（单谱 simai 或 maidata）",
            Arity = ArgumentArity.ExactlyOne
        };

        root.Options.Add(levelsOption);
        root.Arguments.Add(inputArgument);

        root.SetAction(parseResult =>
        {
            var inputPath = parseResult.GetValue(inputArgument)
                ?? throw new InvalidOperationException("缺少参数 inputfile。");
            var levelsRaw = parseResult.GetValue(levelsOption);
            RunConvert(inputPath, levelsRaw);
        });

        return root;
    }

    private static void RunConvert(string inputPath, string? levelsRaw)
    {
        var levelFilter = string.IsNullOrWhiteSpace(levelsRaw)
            ? null
            : ParseLevelList(levelsRaw);

        var ext = Path.GetExtension(inputPath);
        if (string.Equals(ext, ".ma2", StringComparison.OrdinalIgnoreCase))
            throw new NotImplementedException("从 .ma2 输入的转换尚未实现。");

        if (!string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"不支持的输入扩展名「{ext}」。目前仅支持 .txt（simai / maidata）。");

        if (!File.Exists(inputPath))
            throw new ArgumentException($"找不到文件: {inputPath}");

        var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        var text = File.ReadAllText(inputPath, Encoding.UTF8);

        if (LooksLikeMaidata(text))
            ConvertMaidata(text, inputDir, levelFilter);
        else
            ConvertPlainSimai(text, inputDir, levelFilter);
    }

    private static HashSet<int> ParseLevelList(string s)
    {
        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new ArgumentException("-l / --levels 的难度列表不能为空。");

        var set = new HashSet<int>();
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var id) || id <= 0)
                throw new ArgumentException($"无效的难度编号: 「{p}」。");
            set.Add(id);
        }
        return set;
    }

    private static bool LooksLikeMaidata(string text) =>
        text.Contains("&inote_", StringComparison.Ordinal);

    /// <summary>
    /// lv_N 字段：空或仅含数字、点、加号 → 非宴谱；否则（含汉字等）→ 宴谱。
    /// </summary>
    private static bool IsUtageFromLevelString(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return false;
        foreach (var c in level.Trim())
        {
            if (char.IsDigit(c) || c is '.' or '+')
                continue;
            return true;
        }
        return false;
    }

    private static void PrintAlerts(IReadOnlyList<Alert> alerts, string? header = null)
    {
        if (alerts.Count == 0)
            return;
        if (header != null)
            Console.Error.WriteLine(header);
        foreach (var a in alerts)
            Console.Error.WriteLine(a.ToString());
    }

    private static void ConvertMaidata(string text, string outputDir, HashSet<int>? levelFilter)
    {
        var maidata = new Maidata(text);
        var ids = maidata.Levels.Keys.OrderBy(k => k).ToList();
        if (ids.Count == 0)
            throw new ArgumentException("maidata 中未找到任何 &inote_* 谱面。");

        var selected = levelFilter == null
            ? ids
            : ids.Where(id => levelFilter.Contains(id)).ToList();

        if (selected.Count == 0)
            throw new ArgumentException("-l / --levels 指定的难度在文件中均不存在。");

        foreach (var id in selected)
        {
            var chartInfo = maidata.Levels[id];
            var bigTouch = id is 2 or 3;
            var isUtage = IsUtageFromLevelString(chartInfo.Level);
            var ma2 = SimaiToMa2(chartInfo.Inote, maidata.ClockCount, bigTouch, isUtage);
            var outPath = Path.Combine(outputDir, $"lv_{id}.ma2");
            File.WriteAllText(outPath, ma2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void ConvertPlainSimai(string text, string outputDir, HashSet<int>? levelFilter)
    {
        if (levelFilter != null)
            throw new ArgumentException("纯 simai 单谱（非 maidata）不能使用 -l / --levels。");

        const int outputLevel = 0;
        var ma2 = SimaiToMa2(text);
        var outPath = Path.Combine(outputDir, $"lv_{outputLevel}.ma2");
        File.WriteAllText(outPath, ma2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string SimaiToMa2(string inote, int clockCount=4, bool bigTouch=false, bool isUtage=false)
    {
        var (chart, parseAlerts) = new SimaiParser(bigTouch, isUtage, clockCount).Parse(inote);
        var (ma2, genAlerts) = new MA2Generator().Generate(chart);
        var combined = new List<Alert>(parseAlerts.Count + genAlerts.Count);
        combined.AddRange(parseAlerts);
        combined.AddRange(genAlerts);
        PrintAlerts(combined);
        return ma2;
    }
}
