using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;
using MuConvert.generator;
using MuConvert.maidata;
using MuConvert.parser;
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
            Description = $"MuConvert {Utils.AppVersion} — 新一代Simai与MA2互转转谱器\n"
        };

        var levelsOption = new Option<string?>("--levels", "-l")
        {
            Description = "仅转换指定难度（以maidata中的&inote_编号为准），多个难度用逗号分隔；省略则转换全部难度。",
            HelpName = "N[,N...]"
        };

        var inputArgument = new Argument<string>("path")
        {
            Description = "可以输入以下几种情况：\n" +
                          "1.单个.txt文件（标准maidata.txt，或是不含maidata的头信息、直接是Simai的Notes的文件，都可以）。会把它转为MA2。请通过-l指定要转换的谱面难度，不指定则默认转换全部难度。\n" +
                          "2.单个.ma2文件。会把它转为Simai，输出maidata.txt。如果想要转换多个难度，请传入目录，详见第4条。\n" +
                          "3.一个包含有maidata.txt的目录。行为同第一条。\n" +
                          "4.一个包含有一个或多个.ma2文件的目录。会把它们转为一个maidata.txt。请通过-l指定要转换的谱面难度，不指定则默认转换全部难度。",
            Arity = ArgumentArity.ExactlyOne
        };

        root.Options.Add(levelsOption);
        root.Arguments.Add(inputArgument);

        root.SetAction(parseResult =>
        {
            var inputPath = parseResult.GetValue(inputArgument)
                ?? throw new InvalidOperationException("缺少参数 path。");
            var levelsRaw = parseResult.GetValue(levelsOption);
            RunConvert(inputPath, levelsRaw);
        });

        return root;
    }

    private static void RunConvert(string inputPath, string? levelsRaw)
    {
        var fullPath = Path.GetFullPath(inputPath.Trim());

        if (Directory.Exists(fullPath))
            RunConvertDirectory(fullPath, levelsRaw);
        else if (File.Exists(fullPath))
            RunConvertFile(fullPath, levelsRaw);
        else
            throw new ArgumentException($"找不到路径: {inputPath}");
    }

    private static void RunConvertDirectory(string dir, string? levelsRaw)
    {
        var enumOpts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = false
        };

        var maidataPaths = Directory.GetFiles(dir, "maidata.txt", enumOpts);
        var ma2Paths = Directory.GetFiles(dir, "*.ma2", enumOpts);

        var hasMaidata = maidataPaths.Length > 0;
        var hasMa2 = ma2Paths.Length > 0;

        if (hasMaidata && hasMa2)
            throw new ArgumentException("目录中同时存在 maidata.txt 与 .ma2，请只保留其中一种输入。");
        if (!hasMaidata && !hasMa2)
            throw new ArgumentException("目录中未找到 maidata.txt 或 .ma2 文件。");

        if (hasMaidata)
        {
            if (maidataPaths.Length > 1)
                throw new ArgumentException("目录中存在多个 maidata.txt，请只保留一个。");
            RunConvertTxtFile(maidataPaths[0], levelsRaw);
            return;
        }

        var title = new DirectoryInfo(dir).Name;
        ConvertMa2PathsToMaidata(dir, title, ma2Paths, levelsRaw);
    }

    private static void RunConvertFile(string filePath, string? levelsRaw)
    {
        var ext = Path.GetExtension(filePath);
        if (string.Equals(ext, ".ma2", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
            var title = new DirectoryInfo(parent).Name;
            ConvertMa2PathsToMaidata(parent, title, [filePath], levelsRaw);
            return;
        }

        if (string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            RunConvertTxtFile(filePath, levelsRaw);
            return;
        }

        throw new ArgumentException($"不支持的输入扩展名「{ext}」。支持 .txt、.ma2，或目录。");
    }

    private static void RunConvertTxtFile(string inputPath, string? levelsRaw)
    {
        var levelFilter = string.IsNullOrWhiteSpace(levelsRaw) ? null : ParseLevelList(levelsRaw);

        var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        var text = File.ReadAllText(inputPath, Encoding.UTF8);

        if (LooksLikeMaidata(text))
            ConvertMaidata(text, inputDir, levelFilter, inputPath);
        else
            ConvertPlainSimai(text, inputDir, levelFilter, inputPath);
    }

    /// <summary>
    /// 与测试集约定一致：<c>*XX.ma2</c> 中 XX 为游戏难度后缀时，maidata inote = XX + 2；<c>lv_N.ma2</c> 为本工具导出，inote = N。
    /// </summary>
    private static bool TryParseMaidataLevelFromMa2FileName(string filePath, out int levelId)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);

        if (stem.StartsWith("lv_", StringComparison.OrdinalIgnoreCase) && stem.Length > 3 &&
            int.TryParse(stem.AsSpan(3), out var lv) && lv > 0)
        {
            levelId = lv;
            return true;
        }

        var m = Regex.Match(stem, @"(\d{2})$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var suffix))
        {
            levelId = suffix + 2;
            return true;
        }

        levelId = 5;
        return false;
    }

    private static List<(string FullPath, int LevelId)> AssignMaidataLevelsForMa2Files(string[] ma2Paths)
    {
        Array.Sort(ma2Paths, StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<int>();
        var list = new List<(string, int)>(ma2Paths.Length);

        foreach (var path in ma2Paths)
        {
            var suggested = TryParseMaidataLevelFromMa2FileName(path, out var parsed) ? parsed : 5;

            var id = suggested;
            while (used.Contains(id))
                id++;

            used.Add(id);
            list.Add((path, id));
        }

        return list;
    }

    private static void ConvertMa2PathsToMaidata(string outputDir, string title, IReadOnlyList<string> ma2FullPaths, string? levelsRaw)
    {
        if (ma2FullPaths.Count == 0)
            throw new ArgumentException("未提供任何 .ma2 文件。");

        var paths = ma2FullPaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var levelFilter = string.IsNullOrWhiteSpace(levelsRaw) ? null : ParseLevelList(levelsRaw);
        var assignments = AssignMaidataLevelsForMa2Files(paths)
            .OrderBy(t => t.LevelId)
            .Where((_, lv)=> levelFilter == null || levelFilter.Contains(lv))
            .ToList();
        var outPath = Path.Combine(outputDir, "maidata.txt");

        int clockCount = 4;
        var inoteBlocks = new List<(int LevelId, string Inote)>();

        foreach (var (fullPath, levelId) in assignments)
        {
            Console.WriteLine($"Simai → MA2: {fullPath}(lv{levelId}) → {outPath}");
            var ma2Text = File.ReadAllText(fullPath, Encoding.UTF8);
            var (chart, parseAlerts) = new MA2Parser().Parse(ma2Text);
            PrintAlerts(parseAlerts);
            var (simai, genAlerts) = new SimaiGenerator().Generate(chart);
            PrintAlerts(genAlerts);
            inoteBlocks.Add((levelId, simai));
            clockCount = chart.ClockCount;
        }

        var maidata = new Maidata();
        maidata["title"] = title;
        maidata["first"] = "0";
        maidata["clock_count"] = clockCount.ToString();
        foreach (var (levelId, inote) in inoteBlocks)
            maidata.AddLevel(levelId, new MaidataChart(inote));
        File.WriteAllText(outPath, maidata.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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

    private static void ConvertMaidata(string text, string outputDir, HashSet<int>? levelFilter, string inputPath)
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
            var outPath = Path.Combine(outputDir, $"lv_{id}.ma2");
            Console.WriteLine($"Simai → MA2: {inputPath}(lv${id}) → {outPath}");
            var chartInfo = maidata.Levels[id];
            var bigTouch = id is 2 or 3;
            var isUtage = IsUtageFromLevelString(chartInfo.Level);
            var ma2 = SimaiToMa2(chartInfo.Inote, maidata.ClockCount, bigTouch, isUtage);
            File.WriteAllText(outPath, ma2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void ConvertPlainSimai(string text, string outputDir, HashSet<int>? levelFilter, string inputPath)
    {
        if (levelFilter != null)
            throw new ArgumentException("纯 simai 单谱（非 maidata）不能使用 -l / --levels。");

        const int outputLevel = 0;
        var outPath = Path.Combine(outputDir, $"lv_{outputLevel}.ma2");
        Console.WriteLine($"Simai → MA2: {inputPath}(lv${outputLevel}) → {outPath}");
        var ma2 = SimaiToMa2(text);
        File.WriteAllText(outPath, ma2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string SimaiToMa2(string inote, int clockCount=4, bool bigTouch=false, bool isUtage=false)
    {
        var (chart, parseAlerts) = new SimaiParser(bigTouch, clockCount).Parse(inote);
        PrintAlerts(parseAlerts);
        var (ma2, genAlerts) = new MA2Generator(isUtage).Generate(chart);
        PrintAlerts(genAlerts);
        return ma2;
    }
}
