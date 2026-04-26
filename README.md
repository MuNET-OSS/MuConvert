MuConvert - 支持Simai与MA2谱面互转的新一代转谱器
================

MuConvert 是一个支持**Simai与MA2互转**的转谱器。

> Kind reminder: To reduce developers’ workload, this README is maintained only in Chinese. We recommend using an LLM to translate and read this document.

#### 本项目的主要优势
- 特性支持完善：本项目严格按照[Simai语言文档](https://w.atwiki.jp/simai/pages/1002.html)编写，全面支持Simai官方标准的所有特性；同时也加入了许多官方文档未注明、但在自制谱圈被广泛使用的语法，如`||`行内注释，`&demo_seek`，`&clock_count`等。同时也对一些常见的非标准语法具有兼容性。
- 无精度损失：内部使用Rational高精度分数类作为时间等相关运算的基础，确保没有精度损失，不会见到大于384分或无法被384整除的分音就无法处理等。
- 创新采用ANTLR作为Simai的解析器，减少手写解析可能导致的错误等问题，同时保持良好的代码可读、可维护性。
- 工具+基础库：既可以直接当作命令行工具使用，也可以把它作为一个C#依赖库嵌入到你的工程里。
- 可扩展的架构设计：本项目以中间表示(Chart类)为核心，通过为每种语言编写parser、将语言解析为统一的Chart对象的中间表示，再为每种语言编写generator，实现任意两个语言间的互转。尽管目前只支持了Simai和MA2，但项目设计具有良好的可扩展性，您可轻松按照自己的需求定制自己的语言格式，或直接把Chart的解析结果拿来服务于您自己的下游项目如谱面播放器等。

## 使用文档
本项目具有两种使用方式：
- 首先，您可以直接当作命令行工具使用。本项目会编译出名为`MuConvert.exe`的应用程序，详见下面文档的第一部分。
- 此外，本项目也可作为一个C#依赖库，嵌入到您的其他项目里，以库的方式进行使用。详见下面文档的第二部分。

### 1) 直接使用本程序进行转谱（CLI）

#### 如何下载
- 请到 GitHub Actions 的 [`Build` 工作流页面](https://github.com/MuNET-OSS/MuConvert/actions/workflows/build.yml)，打开最新一次分支的构建，在 Artifacts 中下载 `MuConvert.exe即可`。
  - 下载到的`MuConvert.exe`，按照下文所述的用法，从命令行中直接运行即可。
  > 如果你希望自己编译，也可以参考后面“开发者指南”部分的[编译为单文件exe模式](#编译为单文件exe模式)。

#### 基本用法

```shell
MuConvert.exe <path> [-l|--levels N[,N...]] [-o|--output <输出路径或->] [--strict|--lax]
```

- **`path`**：输入路径（必填），可以是 `.txt` / `.ma2` / 目录（见下文）
- **`-l, --levels`**：仅转换指定难度（以 `maidata.txt` 的 `&inote_编号` 为准），多个难度用英文逗号分隔；省略则转换全部难度
- **`-o, --output`**：指定输出位置（可选）；不传入此参数时，文件将保存到“输入文件所在的目录”。
  - 会智能识别你传入的是目录还是文件，做智能的处理，将转谱结果输入到目录下或保存为文件。
  - 此外，还可以传入 `-` ，表示输出到stdout。
- **`--strict` / `--lax`**：控制 Simai 解析器 `SimaiParser` 的严格程度（`StrictLevel`），**仅在 Simai → MA2**时有效。`--strict` 对应严格模式，`--lax` 对应宽松模式；二者**不可同时**指定；均省略时为普通模式（`Normal`）。
  - `--strict`（严格模式）：几乎不会自动修复任何错误，遇到解析错误/非标准语法直接报错退出。
  - 默认模式：会尽力尝试修复一些局部性的错误，并给出警告。但遇到无法修复的错误时则会报错退出。
  - `--lax`（宽松模式）：同上会尽力修复错误，而对于无法修复的错误，会把错误所在的音符**直接吞掉**（并给出警告），以换取解析可以继续向下进行。一般来说除非遇到很严重的问题，不会报错退出。

#### `path` 支持的输入形式与输出规则
通过命令行传入的参数，既可以是文件，也可以是目录。
- **输入 `.txt`（`maidata.txt` 或“纯 simai 单谱”）**：把Simai转为MA2。
  - **如果是 `maidata.txt`（含 `&inote_`）**：会在输入文件的相同目录下，产生 `lv_{id}.ma2`（每个难度一个文件）。
    - 可用类似 `-l 5,6` 的选项，只导出部分难度
  - **如果是纯 simai Notes（不含 maidata 头信息）**：会在输入文件的相同目录下，产生 `lv_0.ma2`。

- **输入 `.ma2` 文件**：把MA2转为Simai。
  - 输出：会在输入文件的相同目录下，产生 `maidata.txt`（当然，里面只有您传入的MA2所对应的一个难度）。
  - 如果想把多张不同难度的 `.ma2` 合并进一个 `maidata.txt`，请直接传入目录（见下一条）。

- **输入目录**：智能识别
  - **目录中包含 `maidata.txt`**：等价于输入该 `maidata.txt`
  - **目录中包含一个或多个 `.ma2`**：将它们合并转为同目录的 `maidata.txt`
  - 若目录中 **同时存在** `maidata.txt` 与 `.ma2`，或两者都不存在，会报错

#### 示例
- **Simai（maidata）→ MA2（按难度导出）**
```shell
MuConvert "D:\charts\MyChart\maidata.txt"
MuConvert "D:\charts\MyChart" # 与上面等价
MuConvert "D:\charts\MyChart\maidata.txt" -l 5,6 # 只转紫谱和白谱
# 生成的转谱结果位于D:\charts\MyChart\lv_X.ma2
```

- **MA2 → Simai（生成/覆盖 `maidata.txt`）**
```shell
MuConvert "D:\charts\MyChart\000000_00.ma2" # 只转一个难度
MuConvert "D:\charts\MyChart" # 转换目录中的所有难度，生成一个maidata.txt文件
MuConvert "D:\charts\MyChart" -l 5,6 # 只转紫谱和白谱
# 生成的转谱结果位于D:\charts\MyChart\maidata.txt
```

### 2) 将本项目作为依赖库使用
#### 导入依赖库
- **推荐做法**：把本仓库作为 git submodule 引入你的工程仓库，然后把 `MuConvert.csproj` 加入你的 `.sln`/`.slnx`。

```shell
git submodule add https://github.com/MuNET-OSS/MuConvert MuConvert # 将本项目的源码导入为submodule
dotnet sln .\YourSolution.sln add .\MuConvert\MuConvert.csproj # 将项目加入解决方案
```

#### 使用方法（TLDR）：
**Simai → MA2**：
```csharp
string maidataText = File.ReadAllText(@"D:\charts\MyChart\maidata.txt", Encoding.UTF8); // maidata.txt 作为字符串
var maidata = new Maidata(maidataText); // 通过 Maidata 模块解析 maidata，得到整张谱的元信息，和每个难度的谱面
var inote = maidata.Levels[5].Inote; // 以紫谱为例，取出该难度的谱面内容（&inote_5）

var (chart, alerts) = new SimaiParser().Parse(inote); // 将 simai 解析为 Chart（中间表示）
var (ma2Text, alerts) = new MA2Generator().Generate(chart); // 将Chart对象导出为MA2的字符串
return ma2Text; // ma2Text即为转谱结果
```

**MA2 → Simai**：
```csharp
string ma2Text = File.ReadAllText(@"D:\charts\MyChart\000000_00.ma2", Encoding.UTF8); // MA2文件，整体读取为字符串
var (chart, alerts) = new MA2Parser().Parse(ma2Text); // 将MA2解析为Chart类的对象（谱面解析结果，中间表示）。alerts是解析时可能产生的警告信息等，建议打印出来。
var (simaiText, alerts) = new SimaiGenerator().Generate(chart); // 将Chart对象导出为Simai语言的字符串

// 注意simaiText这时只是一个纯simai的inotes序列，而不是maidata；需要通过下面的方式构造maidata。
var maidata = new Maidata();
maidata.AddLevel(5, new MaidataChart(inote: simaiText, "13+", "谱师名字")); // 把刚刚转出的谱面的inote添加进去
// 为maidata添加你想要的属性
maidata.Title = "MyChart";
maidata.First = 0;
maidata.ClockCount = chart.ClockCount;
// ... 还可继续添加更多你想要的属性。对于非标准属性，则可以直接用字典的方式添加（Maidata类继承自Dictionary<string, string>）：
maidata["somethingelse"] = "xxx";

var maidataText = maidata.ToString(); // 通过ToString方法将Maidata对象序列化为文本
return maidataText; // maidataText即为转谱结果
```

#### parser和generator的选项
- 部分parser和generator，在其构造参数中带有可选的选项参数，可以控制转谱时的一些行为。
  - SimaiParser带有以下选项：
    - bool `bigTouch` (默认为false): 是否将谱面中的Touch和TouchHold生成为大尺寸。
    - int `clockCount` (默认为4): 控制在谱面开头的“哒哒哒哒”的那几声，有几下。
    - StrictLevelEnum `strictLevel` (默认为 `Normal`): 解析 Simai 时的严格程度（`Strict` / `Normal` / `Lax`），影响语法容错与报错策略。各个严格程度策略的具体含义，请参见上方[CLI文档](#基本用法)中的相关描述。
  - MA2Generator带有以下选项：
    - bool `isUtage` (默认为false): 仅影响生成的MA2的文件头区域的`FES_MODE`的值是1还是0，一般来说是不重要的。

#### 更多示例（异常处理）
注意：当解析/生成步骤失败时，会抛出`ConversionException`异常，其中含有`Alerts`属性，是转谱过程中遇到的错误和警告等信息。（类比于C语言编译器会打印出Error和Warning信息）  
因此，建议您采用try-catch的写法，捕获可能出现的异常，并无论转谱成功失败、总是打印出Alert信息：（下面例子以Simai → MA2为例，如果反过来转则直接更换Parser和Generator即可）
```csharp
using System.Text;
using MuConvert.generator;
using MuConvert.parser;
using MuConvert.utils;

var maidata = new Maidata(File.ReadAllText(@"D:\charts\MyChart\maidata.txt", Encoding.UTF8));
var inote = maidata.Levels[5].Inote; // 以紫谱为例，取出该难度的谱面内容（&inote_5）

List<Alert> alerts = [];
try
{
    var (chart, alerts1) = new SimaiParser().Parse(inote);
    alerts.AddRange(alerts1);
    var (ma2Text, alerts2) = new MA2Generator().Generate(chart);
    alerts.AddRange(alerts2);
    return ma2Text;
}
catch (ConversionException e)
{
    alerts.AddRange(e.Alerts);
    throw;
}
finally
{ // 无论转换成功还是失败，都打印出Alert信息
    foreach (Alert a in alerts) Console.Error.WriteLine(a);
}
```

#### 核心概念（parser / IR / generator）

- **parser（解析器）**：把“源格式文本”解析成中间表示
  - `SimaiParser.Parse(string)` → `Chart`
  - `MA2Parser.Parse(string)` → `Chart`
  - 返回值同时带有 `List<Alert>`；如果遇到致命错误会抛出 `ConversionException`

- **中间表示 IR（`Chart`）**：MuConvert 内部统一的数据结构
  - 入口类型是 `MuConvert.chart.Chart`
  - 关键字段包括 `Chart.BpmList` 与 `Chart.Notes`，以及 `Touch/Hold/Slide` 等具体 `Note` 子类

- **generator（生成器）**：把 `Chart` 转回“目标格式文本”
  - `SimaiGenerator.Generate(Chart)` → simai 文本（可写入 `maidata.txt` 的 `&inote_*`）
  - `MA2Generator.Generate(Chart)` → `.ma2` 文本


## 开发者指南
> 以下内容是面向对于本程序感兴趣，想要了解技术细节/调试bug/参与开发的开发者的。如果你只是普通用户，可以不必阅读以下内容；如果你遇到了bug，请通过[issue](https://github.com/MuNet-OSS/MuConvert/issues)进行反馈。

首先，推荐阅读：[Simai语言文档](https://w.atwiki.jp/simai/pages/1002.html)

### MuConvert的设计理念
- 转谱的本质就是transpiler。MuConvert严格遵循`源语言 ---parser--> 中间表示(IR) ---generator--> 目标语言`的transpiler通用设计模式，以确保代码的清晰和可维护性、减少冗余代码。
  - IR在代码中就是`Chart`类，以及它所引用的`Note`、`BPMList`等子类。
- 然而，对MA2和Simai稍有了解的朋友们都知道，**它们二者的表达能力是不等价的**。（严格说来Simai的表达能力更强，但MA2也有一些独特的、没法简单的等价到Simai语法中的设计）
  - 最简单的一点是，MA2的所有音符都是对齐到Tick，即1/384小节的。你无法把类似`{36}1,1,1,1,`这样的、使用了无法被384整除的分音的Simai，转化为完全等价、不丢失任何信息的MA2格式，它在MA2中只能被近似到最接近的1/384分音上。
  - 此外还有一个重要的区别是，对于持续了一段时间的Hold或Slide、在其持续过程中BPM发生了变化的情况，MA2和Simai的定义也是完全不一样的。MA2在把“小节时间”计算为绝对时间时，会严格按照BPM表的声明、考虑BPM的变化；而Simai则被规定为仅按照音符开始时刻的BPM为基准，不考虑BPM的变化。详见下文[关于时间格式](#关于时间格式)部分所述。
- 因此，MuConvert的另一个设计目标是：在中间表示(IR)中不丢失任何信息。
  - 从源语言到IR的过程，即parser，确保是**无损**的、可以记录下来关于这个谱面的所有信息。这样可以有利于维护，也为将来的发展提供了更大的可扩展性。
  - 而在从IR到目标语言的过程，即genertaor，则会根据目标语言的表达能力，进行必要的**近似**，所有的信息丢失都发生在generator中。

### 编译为单文件exe模式
> 这样就不用带着一堆依赖一起发给别人了，只发一个exe就行。
```shell
dotnet publish -c Release -r win-x64 -p:SelfContained=false -p UseAppHost=true -p:PublishSingleFile=true
```
注意：以上命令以Release模式编译，且设置了`SelfContained=false`，即不会把.NET运行时打包进来，这样出来的exe只有几M大，但要求用户电脑上必须有.NET 10 Runtime才能运行。  
如果有必要，可自行将`SelfContained`改为true，这样就会打包一个完整的.NET运行时进去（出来的exe有大几十M），但是确保用户可以运行。

### 关于时间格式
- 与其他库可能有所不同的一点是，本程序的底层使用“小节时间”(`Bar`)作为核心的时间格式表示。
  - “小节时间”是一个**分数**，指从谱面开头所经过的小节数。
    - 选择这种方式的核心考量是，MA2底层本质上就是一个基于“小节时间”的格式，而Simai虽然两种格式都支持、但最为常见和常用的也是`(180){4},`和`1h[4:1]`这种以小节为单位计时的情况，真正会写绝对时间的人是很少的。
  - 同时，我们也对“绝对时间”有着良好的支持，以便解析类似`1-3[0.1##0.3]`这种绝对指定了星星时长的代码。
    - 其原理是，我们定义了一个`Duration`类，它会在底层以实际输入的数据进行存储，并进行全自动的计算和转换。
  - 此外，还有一个重要的问题：对于`Hold`和`Slide`，如果持续过程中发生了BPM变化的话，`simai`和`MA2`对此的定义是完全不一样的。
    - `MA2`是会严格按照BPM表的声明来把“小节时间”计算为绝对时间，考虑BPM的变化；而`simai`是会按照音符开始时刻的BPM为基准，把“小节时间”计算为绝对时间，不考虑BPM的变化。
    - 举一个具体的例子：对下述的总长为一小节、但横跨120BPM和60BPM区间的星星，
      - 对`(120){2}1h[1:1],(60){2},`在`simai`中这个hold的时长是120BPM下的1小节即2s；
      - 然而对看似等价的表述`BPM 0 0 120; BPM 0 192 60; NMHLD 0 0 0 384`，这个hold的持续时长是它落在120BPM的那半小节(1s)+落在60BPM的那半小节(2s)，总共是3s。
    - 因此，在`Duration`中会进一步的把底层的数据存储类型分为`Bar`和`InvarientBar`，对应于以上的两种情况。

### 关于parser的技术选型
- SimaiParser，考虑到Simai是一个相对复杂的格式化语法、本质是一种DSL，所以采用了[ANTLR](https://github.com/antlr/antlr4/blob/master/doc/index.md)进行解析。
  - ANTLR的核心是`g4`语法文件，因此在我们的代码里编写了针对Simai语言的语法定义文件：`Simai.g4`。如需修改，请自行学习ANTLR的文档。
  - ANTLR本身是不特定于编程语言的（它提供了各种编程语言的SDK），而语法文件的作用是，会被用于生成目标编程语言的“解析器代码”，以供调用。
    - 在`MuConvert.csproj`中定义了一个`<Antlr4>`的Item，它就是用来在编译时添加一个从语法文件生成C#解析器代码的编译步骤的。生成的文件会被自动放在`obj`目录下。
    - 具体的原理，请详见`parser/simai/SimaiParser.cs`中，对`MuConvert.antlr`下的各个类的引用。
- MA2的话，由于其天生就是为了机读设计的、格式相对简单，没有必要上ANTLR；而是直接逐行读取、一行内`Split('\t')`，就足以解析MA2的所有内容了。

### 多语言(i18n)相关
- 本项目中支持基于`System.Globalization`的多语言，语言文件位于`i18n`目录中。
- 其中，一级支持语言为三个（即[MaiChartManager](https://github.com/MuNET-OSS/MaiChartManager)支持的语言）：简体中文(`Locale.zh.resx`)、英语(`Locale.resx`)、繁体中文(`Locale.zh-hant.resx`)。
  - 一般的开发过程，包括有意提交PR的人在实现代码时需要新增/修改i18n key的，只需处理这三种语言文件即可。
- 其他的为二级支持语言，一般的开发过程可以不必处理和新增这些语言的翻译key；Maintainers会定期的将一级支持语言的翻译内容（通过LLM机器翻译）同步到这些语言中。
  - 当然，如果你发现翻译有错误，也可以直接提PR修改。