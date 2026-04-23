using MuConvert.generator;
using MuConvert.parser;
using Xunit.Abstractions;

namespace MuConvert.Tests;

/// <summary>
/// 针对另一项目中 <c>FixChartSimaiSharp</c> / <c>SimaiParser.Preprocess</c> 计划支持的常见非标准 Simai 写法。
/// 每个用例比较：纠错后应与「规范写法」解析得到的 MA2 完全一致。
/// 另含带 <c>#</c>、<c>||</c> 行注释的谱面（见 <see cref="Comment_Cases"/> / <see cref="含有注释"/>），与 Preprocess 去注释行为对齐。
/// 在 <see cref="SimaiParser.Preprocess"/> 尚未接入对应替换逻辑前，本文件中的测试失败或抛错属于预期行为（TDD）。
/// </summary>
public class Simai预处理纠错测试
{
    private readonly ITestOutputHelper _output;

    public Simai预处理纠错测试(ITestOutputHelper output) => _output = output;

    /// <summary>规范 simai → 完整 MA2 文本（含头与 BPM）。</summary>
    private string SimaiToMa2(string inote, bool DontTryFix = false)
    {
        var parser = new SimaiParser(strictLevel: DontTryFix ? SimaiParser.StrictLevelEnum.Strict : SimaiParser.StrictLevelEnum.Normal);
        var (chart, alerts) = parser.Parse(inote);
        var (ma2, alerts2) = new MA2Generator().Generate(chart);
        _output.WriteLine(string.Join('\n', alerts));
        _output.WriteLine(string.Join('\n', alerts2));
        return ma2;
    }

    public static IEnumerable<object[]> TryFix_Cases()
    {
        // SimaiError1: 数字与小节拍号 {n} 之间漏写逗号（时间轴被粘在一起）
        yield return
        [
            "SimaiError1 数字与{拍号}之间缺逗号",
            "(120){4}1,{8}2,E",
            "(120){4}1{8}2,E"
        ];

        // SimaiError2: 时长里误用连字符，应为 beats 的冒号记法
        yield return
        [
            "SimaiError2 时长 [a-b] → [a:b]",
            "(120){4}1h[8:3],E",
            "(120){4}1h[8-3],E"
        ];

        // SimaiError3: 数字与下一段 BPM 的 '(' 粘在一起，应在前面补逗号
        yield return
        [
            "SimaiError3 数字与 '(' BPM 缺逗号",
            "(120){4}1,(60){4}2,E",
            "(120){4}1(60){4}2,E"
        ];

        // SimaiError5: Touch Slide 等处误写 qx，应为 xq
        yield return
        [
            "SimaiError5 qx → xq（Touch Slide 示例）",
            "(120){4}1xq4[1:1],E",
            "(120){4}1qx4[1:1],E"
        ];

        // SimaiError6: 时长括号后的 bxfh 修饰应挪到时长之前（与 1h[2:1]bx → 1hbx[2:1] 同类）。
        // 当前 ANTLR 语法在语义层合并多段 modifiers，本用例可能已通过；仍保留以防将来行为变化。
        yield return
        [
            "SimaiError6 [时长] 后紧跟 bxfh → 挪到时长前",
            "(120){4}1hbx[2:1],E",
            "(120){4}1h[2:1]bx,E"
        ];

        // FixChartSimaiSharp: {{ }}、}} 折叠（先处理 {{/}} 再跑其它替换，避免破坏结构）
        yield return
        [
            "双花括号 {{ }} 折叠为单花括号",
            "(120){4}1,E",
            "(120){{4}}1,E"
        ];
        yield return
        [
            "双闭括号 }} 折叠（拍号后多写一个 }）",
            "(120){4}1,E",
            "(120){4}}1,E"
        ];

        // FixChartSimaiSharp: 不飞星「-?」与「?-」顺序
        yield return
        [
            "-? → ?-（不飞星）",
            "(120){4}1?-3[1:1],E",
            "(120){4}1-?3[1:1],E"
        ];
        
        yield return
        [
            "多打了一个双押符号",
            "(120){4}1/2,2/3,4,E",
            "(120){4}1//2,2//3,4,E"
        ];
    }
    
    public static IEnumerable<object[]> Comment_Cases()
    {
        // 去掉换行
        yield return
        [
            "去除 \\r\\n（与单行等价）",
            "(120){4}1,E",
            "(120)\r\n\n{4}\r\n\n\r\n\n\n\r\n1,\r\nE"
        ];
        
        // 注释：# 与 ||（与 <see cref="SimaiParser.Preprocess"/> 内 commentRegex 行为一致，RegexOptions.Multiline）
        yield return
        [
            "带 # 行首整行注释",
            "(120){4}1,E",
            "# 小节说明\n(120){4}1,E"
        ];
        yield return
        [
            "带 || 行首整行注释",
            "(120){4}1,E",
            "|| 作者留言\n(120){4}1,E"
        ];
        yield return
        [
            "行尾 # 注释（谱面与无注释规范串等价）",
            "(120){4}1,E",
            "(120){4}1,E # 行尾说明"
        ];
        yield return
        [
            "行尾 || 注释",
            "(120){4}1,E",
            "(120){4}1,E || 行尾说明"
        ];
        yield return
        [
            "多行：# 与 || 混排且含空行",
            "(120){4}1,{8}2,E",
            "# A part\n|| B part\n\n(120){4}1,{8}2,E\n|| tail\n# eof"
        ];
        // 绝对时间步 {#n} 中的 # 不应被当成注释（Preprocess 内负向后顾）
        yield return
        [
            "保留 {#…} 内的 #（非注释）",
            "(120){4}{#96}1,E",
            "# 上一行是整行注释\n(120){4}{#96}1,E"
        ];
    }

    [Theory]
    [MemberData(nameof(TryFix_Cases))]
    public void 不规范语法(string description, string canonicalSimai, string malformedSimai)
    {
        _output.WriteLine(description);

        string expected;
        try
        {
            expected = SimaiToMa2(canonicalSimai, true);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Canonical simai failed (fix test data): {description}", e);
        }

        var actual = SimaiToMa2(malformedSimai);
        Assert.Equal(expected, actual);
    }
    
    [Theory]
    [MemberData(nameof(Comment_Cases))]
    public void 含有注释(string description, string canonicalSimai, string malformedSimai)
    {
        _output.WriteLine(description);

        string expected;
        try
        {
            expected = SimaiToMa2(canonicalSimai, true);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Canonical simai failed (fix test data): {description}", e);
        }

        var actual = SimaiToMa2(malformedSimai, true);
        Assert.Equal(expected, actual);
    }
}
