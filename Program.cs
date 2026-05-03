using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;
using MuConvert.chu;
using MuConvert.mai;
using MuConvert.utils;

namespace MuConvert;

internal static class Program
{
    private static int Main(string[] args)
    {
        var root = BuildRootCommand();
        try
        {
            var parseResult = root.Parse(args);
            var invocation = new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
            };
            return parseResult.Invoke(invocation);
        }
        catch (ConversionException ex)
        {
            PrintAlerts(ex.Alerts, "转换失败：");
            Console.Error.WriteLine("转换失败！报错详见如上。您可以通过 https://github.com/MuNet-OSS/MuConvert/issues 反馈问题。");
#if DEBUG
            Console.Error.WriteLine(ex);
#endif
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
            Description = $"MuConvert {Utils.AppVersion} — 新一代多功能音游转谱器\n" +
                          $"使用文档详见：https://github.com/MuNET-OSS/MuConvert/blob/master/README.md"
        };

        var levelsOption = new Option<string?>("--levels", "-l")
        {
            Description = "仅转换指定难度，多个难度用逗号分隔；省略则转换全部难度。",
            HelpName = "N[,N...]"
        };

        var targetOption = new Option<string?>("--target", "-t")
        {
            Description = "强制指定输出格式。目前仅有C2S->SUS必须指定本参数，其他情况省略使用默认值即可。",
            HelpName = "format"
        };

        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = "指定输出位置。可指定文件或目录，或\"-\"(stdout)；不指定则默认为输入文件所在目录。",
            HelpName = "path"
        };

        var strictOption = new Option<bool>("--strict")
        {
            Description = "解析使用严格模式（仅在Simai转MA2模式下有效）",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false
        };

        var laxOption = new Option<bool>("--lax")
        {
            Description = "解析使用宽松模式（仅在Simai转MA2模式下有效）",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false
        };

        var inputArgument = new Argument<string>("path")
        {
            Description = "可以输入文件或目录。会自动根据输入的类型，智能执行相应的转换程序。\n" +
                          "例如，输入一个包含多个.ma2文件的目录，则会把各个难度合并转为一个maidata.txt。",
            Arity = ArgumentArity.ExactlyOne
        };

        root.Options.Add(levelsOption);
        root.Options.Add(targetOption);
        root.Options.Add(outputOption);
        root.Options.Add(strictOption);
        root.Options.Add(laxOption);
        root.Arguments.Add(inputArgument);

        root.SetAction(parseResult =>
        {
            var inputPath = parseResult.GetValue(inputArgument)
                ?? throw new InvalidOperationException("缺少参数 path。");
            var levelsRaw = parseResult.GetValue(levelsOption);
            var targetRaw = parseResult.GetValue(targetOption);
            _cliTargetNormalized = string.IsNullOrWhiteSpace(targetRaw) ? null : targetRaw.Trim().ToLowerInvariant();
            _outputSpec = OutputSpec.Parse(parseResult.GetValue(outputOption));

            var cliStrict = parseResult.GetValue(strictOption);
            var cliLax = parseResult.GetValue(laxOption);
            if (cliStrict && cliLax) throw new ArgumentException("不能同时指定 --strict 与 --lax。");
            else if (cliStrict) _simaiStrictLevel = SimaiParser.StrictLevelEnum.Strict;
            else if (cliLax) _simaiStrictLevel = SimaiParser.StrictLevelEnum.Lax;

            RunConvert(inputPath, levelsRaw);
        });

        return root;
    }

    /// <summary>由 CLI 在每次 <c>SetAction</c> 入口赋值；转换逻辑只读此字段。</summary>
    private static OutputSpec _outputSpec;
    private static SimaiParser.StrictLevelEnum _simaiStrictLevel = SimaiParser.StrictLevelEnum.Normal;
    
    /// <summary>由 CLI 赋值；为 null 表示按输入类型使用默认输出格式，否则为小写的目标格式名（如 sus、ma2）。</summary>
    private static string? _cliTargetNormalized;

    private enum OutputSinkKind { Default, Stdout, Directory, File }
    
    private readonly record struct OutputSpec(OutputSinkKind Kind, string? FsPath)
    {
        internal static OutputSpec Parse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new OutputSpec(OutputSinkKind.Default, null);
            var t = raw.Trim();
            if (t == "-")
                return new OutputSpec(OutputSinkKind.Stdout, null);
            var full = Path.GetFullPath(t);
            if (Directory.Exists(full))
                return new OutputSpec(OutputSinkKind.Directory, full);
            if (File.Exists(full))
                return new OutputSpec(OutputSinkKind.File, full);
            if (!string.IsNullOrEmpty(Path.GetExtension(full)))
                return new OutputSpec(OutputSinkKind.File, full);
            return new OutputSpec(OutputSinkKind.Directory, full);
        }

        internal string ResolveOutputDir(string defaultDir) =>
            Kind == OutputSinkKind.Directory ? FsPath! : defaultDir;
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
    
    private static readonly string[] supportedPostfixs = new[] { "maidata.txt", ".ma2", ".c2s", ".ugc", ".sus" };

    private static void RunConvertDirectory(string dir, string? levelsRaw)
    {
        var enumOpts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = false
        };
        var inputPaths = Directory.EnumerateFiles(dir, "*", enumOpts)
            .Where(file => supportedPostfixs.Any(file.EndsWith)).ToArray();

        if (inputPaths.Length > 1)
        {
            if (inputPaths.All(file=>file.EndsWith(".ma2")))
            { // 只有多个MA2这种情况是允许的，直接调用ConvertMa2PathsToMaidata
                var title = new DirectoryInfo(dir).Name;
                ConvertMa2PathsToMaidata(dir, title, inputPaths, levelsRaw);
            }
            else
            {
                throw new ArgumentException($"目录中存在多种/多个谱面文件：{string.Join(", ", inputPaths)}。请直接指定到具体的文件路径，或者删除多余的文件。");
            }
        }
        else RunConvertFile(inputPaths[0], levelsRaw);
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

        if (string.Equals(ext, ".c2s", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".ugc", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".sus", StringComparison.OrdinalIgnoreCase))
        {
            if (levelsRaw != null) throw new ArgumentException("-l / --levels 仅适用于 maimai 的 maidata 或目录中的 .ma2，不适用于中二谱（.c2s / .ugc / .sus）。");
            AssertStrictLaxOnlyForSimaiToMa2(" 中二谱（.c2s / .ugc / .sus）");
            var kind = ext.TrimStart('.').ToLowerInvariant();
            RunConvertChuSingleFile(filePath, kind);
            return;
        }

        throw new ArgumentException($"不支持的输入扩展名「{ext}」。支持 .txt、.ma2、.c2s、.ugc、.sus，或目录。");
    }

    private static void RunConvertTxtFile(string inputPath, string? levelsRaw)
    {
        var levelFilter = string.IsNullOrWhiteSpace(levelsRaw) ? null : ParseLevelList(levelsRaw);

        var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        var text = File.ReadAllText(inputPath, Encoding.UTF8);

        var targetFormat = _cliTargetNormalized ?? "ma2";
        if (targetFormat != "ma2") throw new ArgumentException($"不支持的输出类型「{targetFormat}」。输入文件为simai时，输出格式仅支持ma2。");

        if (LooksLikeMaidata(text))
        {
            var maidata = new Maidata(text);
            var ids = maidata.Levels.Keys.OrderBy(k => k).ToList();
            if (ids.Count == 0) throw new ArgumentException("maidata 中未找到任何 &inote_* 谱面。");
            var selected = levelFilter == null ? ids : ids.Where(id => levelFilter.Contains(id)).ToList();
            if (selected.Count == 0) throw new ArgumentException("-l / --levels 指定的难度在文件中均不存在。");
            ValidateOutputForMa2Targets(selected.Count);
            
            ConvertMaidata(maidata, selected, inputDir, inputPath);
        }
        else
        {
            if (levelFilter != null) throw new ArgumentException("纯 simai 单谱（非 maidata）不能使用 -l / --levels。");
            ValidateOutputForMa2Targets(1);
            ConvertPlainSimai(text, inputDir, inputPath);
        }
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
        AssertStrictLaxOnlyForSimaiToMa2(" MA2 转 Simai");
        
        var targetFormat = _cliTargetNormalized ?? "simai";
        if (targetFormat != "simai") throw new ArgumentException($"不支持的输出类型「{targetFormat}」。输入文件为ma2时，输出格式仅支持simai。");

        var paths = ma2FullPaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var levelFilter = string.IsNullOrWhiteSpace(levelsRaw) ? null : ParseLevelList(levelsRaw);
        var assignments = AssignMaidataLevelsForMa2Files(paths)
            .OrderBy(t => t.LevelId)
            .Where((_, lv)=> levelFilter == null || levelFilter.Contains(lv))
            .ToList();

        if (assignments.Count == 0) throw new ArgumentException("-l / --levels 过滤后没有可转换的 .ma2 文件。");
        ValidateOutputForMaidataTxt();

        var baseDir = _outputSpec.ResolveOutputDir(outputDir);
        var diskPath = _outputSpec.Kind == OutputSinkKind.File ? _outputSpec.FsPath! : Path.Combine(baseDir, "maidata.txt");
        var destNote = _outputSpec.Kind == OutputSinkKind.Stdout ? "（标准输出）" : diskPath;

        int clockCount = 4;
        var inoteBlocks = new List<(int LevelId, string Inote)>();

        foreach (var (fullPath, levelId) in assignments)
        {
            Console.Error.WriteLine($"MA2 → Simai: {fullPath}(lv{levelId}) → {destNote}");
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
            maidata.AddLevel(levelId, new MaidataLevel(inote));

        var maidataText = maidata.ToString();
        if (_outputSpec.Kind == OutputSinkKind.Stdout) Console.Out.Write(maidataText);
        else File.WriteAllText(diskPath, maidataText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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

    private static void ConvertMaidata(Maidata maidata, IReadOnlyList<int> selected, string inputDir, string inputPath)
    {
        var baseDir = _outputSpec.ResolveOutputDir(inputDir);
        foreach (var id in selected)
        {
            var outPath = _outputSpec.Kind == OutputSinkKind.File ? _outputSpec.FsPath! : Path.Combine(baseDir, $"lv_{id}.ma2");
            var destNote = _outputSpec.Kind == OutputSinkKind.Stdout ? "（标准输出）" : outPath;
            Console.Error.WriteLine($"Simai → MA2: {inputPath}(lv{id}) → {destNote}");
            var chartInfo = maidata.Levels[id];
            var bigTouch = id is 2 or 3;
            var isUtage = IsUtageFromLevelString(chartInfo.Level);
            var ma2 = SimaiToMa2(chartInfo.Inote, maidata.ClockCount, bigTouch, isUtage, _simaiStrictLevel);
            if (_outputSpec.Kind == OutputSinkKind.Stdout) Console.Out.Write(ma2);
            else File.WriteAllText(outPath, ma2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void ConvertPlainSimai(string text, string inputDir, string inputPath)
    {
        const int outputLevel = 0;
        var baseDir = _outputSpec.ResolveOutputDir(inputDir);
        var outPath = _outputSpec.Kind == OutputSinkKind.File ? _outputSpec.FsPath! : Path.Combine(baseDir, $"lv_{outputLevel}.ma2");
        var destNote = _outputSpec.Kind == OutputSinkKind.Stdout ? "（标准输出）" : outPath;
        Console.Error.WriteLine($"Simai → MA2: {inputPath}(lv{outputLevel}) → {destNote}");
        var ma2 = SimaiToMa2(text, strictLevel: _simaiStrictLevel);
        if (_outputSpec.Kind == OutputSinkKind.Stdout) Console.Out.Write(ma2);
        else File.WriteAllText(outPath, ma2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void ValidateOutputForMa2Targets(int ma2FileCount)
    {
        if (_outputSpec.Kind == OutputSinkKind.Stdout && ma2FileCount != 1)
            throw new ArgumentException($"-o \"-\" 仅适用于恰好输出一个 MA2 文件的情况（当前会输出 {ma2FileCount} 个）。请通过-l指定难度，或改为指定-o为一个目录。");
        if (_outputSpec.Kind == OutputSinkKind.File && ma2FileCount != 1)
            throw new ArgumentException($"使用 -o 指定输出为文件时，本次必须只生成一个 MA2 文件（当前会生成 {ma2FileCount} 个）。请通过-l指定难度，或改为指定-o为一个目录。");
        if (_outputSpec.Kind == OutputSinkKind.File)
            ValidateOutputFileExtension(_outputSpec.FsPath!, ".ma2");
    }

    private static void ValidateOutputForMaidataTxt()
    {
        if (_outputSpec.Kind == OutputSinkKind.File)
            ValidateOutputFileExtension(_outputSpec.FsPath!, ".txt");
    }

    private static void ValidateOutputFileExtension(string filePath, string requiredExt)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.Equals(ext, requiredExt, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"输出文件扩展名须为「{requiredExt}」，当前为「{(string.IsNullOrEmpty(ext) ? "(无)" : ext)}」。");
    }

    private static void AssertStrictLaxOnlyForSimaiToMa2(string contextSuffix)
    {
        if (_simaiStrictLevel != SimaiParser.StrictLevelEnum.Normal)
            throw new ArgumentException($"--strict / --lax 仅适用于 Simai（.txt / maidata 或纯 inote）转 MA2，不能用于{contextSuffix}。");
    }

    private static readonly Dictionary<string, string[]> chuTargetsDict = new()
    {
        ["c2s"] = ["ugc", "sus"],
        ["ugc"] = ["c2s", "sus"],
        ["sus"] = ["c2s"],
    };
    
    private static void ValidateOutputForSingleChuText(string inputFormat, string targetFormat)
    {
        var validTargets = chuTargetsDict.GetValueOrDefault(inputFormat) ?? [];
        if (!validTargets.Contains(targetFormat)) throw new ArgumentException($"不支持的输出类型「{targetFormat}」。输入文件为{inputFormat}时，输出格式仅支持{validTargets}。");

        if (_outputSpec.Kind == OutputSinkKind.Stdout) return;
        if (_outputSpec.Kind == OutputSinkKind.File)
            ValidateOutputFileExtension(_outputSpec.FsPath!, "." + targetFormat);
    }

    private static void RunConvertChuSingleFile(string filePath, string inputKind)
    {
        var targetFormat = _cliTargetNormalized ?? chuTargetsDict[inputKind][0];
        ValidateOutputForSingleChuText(inputKind, targetFormat);

        var full = Path.GetFullPath(filePath);
        var inputDir = Path.GetDirectoryName(full)!;
        var text = File.ReadAllText(full, Encoding.UTF8);
        
        var baseDir = _outputSpec.ResolveOutputDir(inputDir);
        var outPath = _outputSpec.Kind == OutputSinkKind.File ? _outputSpec.FsPath! : Path.Combine(baseDir, Path.GetFileNameWithoutExtension(full) + "." + targetFormat);
        var destNote = _outputSpec.Kind == OutputSinkKind.Stdout ? "（标准输出）" : outPath;
        Console.Error.WriteLine($"{inputKind.ToUpperInvariant()} → {targetFormat.ToUpperInvariant()}: {full} → {destNote}");
        
        ChuChart chart;
        List<Alert> parseAlerts;
        switch (inputKind)
        {
            case "c2s":
                (chart, parseAlerts) = new C2sParser().Parse(text);
                break;
            case "ugc":
                (chart, parseAlerts) = new UgcParser().Parse(text);
                break;
            case "sus":
                (chart, parseAlerts) = new SusParser().Parse(text);
                break;
            default:
                throw new ArgumentException($"内部错误：未知中二输入种类「{inputKind}」。");
        }
        PrintAlerts(parseAlerts);

        string outText;
        List<Alert> genAlerts;
        switch (targetFormat)
        {
            case "ugc":
                (outText, genAlerts) = new UgcGenerator().Generate(chart);
                break;
            case "sus":
                (outText, genAlerts) = new SusGenerator().Generate(chart);
                break;
            case "c2s":
                (outText, genAlerts) = new C2sGenerator().Generate(chart);
                break;
            default:
                throw new ArgumentException($"内部错误：未实现的中二输出类型「{targetFormat}」。");
        }
        PrintAlerts(genAlerts);
        
        if (_outputSpec.Kind == OutputSinkKind.Stdout) Console.Out.Write(outText);
        else File.WriteAllText(outPath, outText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string SimaiToMa2(string inote, int clockCount = 4, bool bigTouch = false, bool isUtage = false,
        SimaiParser.StrictLevelEnum strictLevel = SimaiParser.StrictLevelEnum.Normal)
    {
        var (chart, parseAlerts) = new SimaiParser(bigTouch, clockCount, strictLevel).Parse(inote);
        PrintAlerts(parseAlerts);
        var (ma2, genAlerts) = new MA2Generator(isUtage).Generate(chart);
        PrintAlerts(genAlerts);
        return ma2;
    }
}
