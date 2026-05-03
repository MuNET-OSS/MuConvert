MuConvert - 支持Simai与MA2谱面互转的新一代转谱器
================

MuConvert 是一个支持**Simai与MA2互转**的转谱器。

> Kind reminder: To reduce developers’ workload, this README is maintained only in Chinese. We recommend using an LLM to translate and read this document.

#### 本项目的主要优势
- 特性支持完善：本项目严格按照[Simai语言文档](https://w.atwiki.jp/simai/pages/1002.html)编写，全面支持Simai官方标准的所有特性；同时也加入了许多官方文档未注明、但在自制谱圈被广泛使用的语法，如`||`行内注释，`&demo_seek`，`&clock_count`等。同时也对一些常见的非标准语法具有兼容性。
- 无精度损失：内部使用Rational高精度分数类作为时间等相关运算的基础，确保没有精度损失，不会见到大于384分或无法被384整除的分音就无法处理等。
- 创新采用ANTLR作为Simai的解析器，减少手写解析可能导致的错误等问题，同时保持良好的代码可读、可维护性。
- 工具+基础库：既可以直接当作命令行工具使用，也可以把它作为一个C#依赖库嵌入到你的工程里。
- 可扩展的架构设计：本项目以中间表示(Chart类)为核心，通过为每种语言编写parser、将语言解析为统一的中间表示对象，再为每种语言编写generator，实现任意两个语言间的互转。
  - 项目设计具有良好的可扩展性，您可轻松按照自己的需求定制自己的语言格式，也可直接把解析得到的Chart对象拿来服务于您自己的下游项目如谱面播放器等。
- 多游戏支持：基于上述良好的可扩展性，本项目一套代码可提供对maimai、chunithm两款游戏共五种格式的支持，未来还可能加入ongeki等更多游戏。

## 使用文档
本项目具有两种使用方式：
- 首先，您可以直接当作命令行工具使用。本项目会编译出名为`MuConvert.exe`的应用程序，详见下面文档的第一部分。
- 此外，本项目也可作为一个C#依赖库，嵌入到您的其他项目里，以库的方式进行使用。详见下面文档的第二部分。

### 1) 直接使用本程序进行转谱（CLI）

#### 如何下载
- 请到 GitHub Actions 的 [`Build` 工作流页面](https://github.com/MuNET-OSS/MuConvert/actions/workflows/build.yml)，打开最新一次分支的构建，在 Artifacts 中下载 `MuConvert.exe` 即可。
  - 下载到的`MuConvert.exe`，按照下文所述的用法，从命令行中直接运行即可。
  > 如果你希望自己编译，也可以参考后面“开发者指南”部分的[编译为单文件exe模式](#编译为单文件exe模式)。

#### 基本用法

```shell
MuConvert.exe <path> [-l|--levels N[,N...]] [-t|--target <format>] [-o|--output <输出路径或->] [--strict|--lax]
```

- **`path`**：输入路径（必填），可以是单个文件或目录，输入目录时会自动找到和处理目录下的谱面文件（详见下文）。
- **`-l, --levels`**：仅转换指定难度（以 `maidata.txt` 的 `&inote_编号` 为准），多个难度用英文逗号分隔；省略则转换全部难度
- **`-t, --target`**：强制指定输出格式（不区分大小写）。
  - 多数情况下不需要指定，直接使用默认值即可。默认值根据输入类型的不同而不同，但一般来说能满足常见的场景需求。
    - 具体而言，默认的转换输出格式为：Simai → `ma2`，MA2 → `simai`，C2S → `ugc`，UGC/SUS → `c2s`。
  - 目前仅有一种情况是必须指定该参数的：即想要C2S转SUS的情况，必须指定`-t sus`（否则默认转出来的是UGC）
- **`-o, --output`**：指定输出位置（可选）；不传入此参数时，文件将保存到“输入文件所在的目录”。
  - 会智能识别你传入的是目录还是文件，做智能的处理，将转谱结果输入到目录下或保存为文件。
  - 此外，还可以传入 `-` ，表示输出到stdout。
- **`--strict` / `--lax`**：控制 Simai 解析器 `SimaiParser` 的严格程度（`StrictLevel`），**仅在 Simai → MA2**时有效。`--strict` 对应严格模式，`--lax` 对应宽松模式；二者**不可同时**指定；均省略时为普通模式（`Normal`）。
  - `--strict`（严格模式）：几乎不会自动修复任何错误，遇到解析错误/非标准语法直接报错退出。
  - 默认模式：会尽力尝试修复一些局部性的错误，并给出警告。但遇到无法修复的错误时则会报错退出。
  - `--lax`（宽松模式）：同上会尽力修复错误，而对于无法修复的错误，会把错误所在的音符**直接吞掉**（并给出警告），以换取解析可以继续向下进行。一般来说除非遇到很严重的问题，不会报错退出。

#### `path` 支持的输入形式与输出规则
通过命令行传入的参数，既可以是文件，也可以是目录。
- **输入单个 maimai 相关格式文件**（`.txt` / `.ma2`）时：
  - **输入 `.txt`**：把Simai转为MA2。
    - **如果是 `maidata.txt`（含 `&inote_`）**：会在输入文件的相同目录下，产生 `lv_{id}.ma2`（每个难度一个文件）。
      - 可用类似 `-l 5,6` 的选项，只导出部分难度。
    - **如果是纯 simai Notes（不含 maidata 头信息）**：会在输入文件的相同目录下，产生 `lv_0.ma2`。
  - **输入 `.ma2` 文件**：把MA2转为Simai。
    - 输出：会在输入文件的相同目录下，产生 `maidata.txt`（当然，里面只有您传入的MA2所对应的一个难度）。
    - 如果想把多张不同难度的 `.ma2` 合并进一个 `maidata.txt`，请直接传入目录（见下一条）。

- **输入单个 CHUNITHM 相关格式文件**（`.c2s` / `.ugc` / `.sus`）时：在 C2S、UGC、SUS之间互转。
  - 不指定 `-t` 时，默认：`.c2s` → 同目录下同名 `.ugc`；`.ugc` 或 `.sus` → 同目录下同名 `.c2s`。
  - 如果想从 C2S 转出 SUS ，则须显式指定 `-t sus`。

- **输入目录**时：会尝试在该目录下寻找谱面文件：
  - 如果找到恰好一个：则等价于上面的输入单个文件的情况、处理这一个文件。
  - 如果找到多个：
    - 如果都是MA2文件，会把这多张不同难度的 `.ma2`谱面转为simai，并合并进同一个 `maidata.txt`。
    - 否则，则是输入不明确的情况，会报错退出。

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

<details>
<summary><strong>CHUNITHM转谱相关示例</strong></summary>

**UGC/SUS → C2S**（默认输出同名 `.c2s`）

```shell
MuConvert "D:\charts\Song\0003_00.ugc" # UGC -> C2S
MuConvert "D:\charts\Song\0003_00.sus" # SUS -> C2S
# 转谱结果与输入同目录，生成 0003_00.c2s

MuConvert "D:\charts\Song\0003_00.ugc" -t sus # 也可 UGC直接 -> C2S
```

**C2S → UGC / SUS**

```shell
MuConvert "D:\charts\Song\0003_00.c2s"
# 默认同目录生成同名 .ugc

MuConvert "D:\charts\Song\0003_00.c2s" -t sus
# 需要 SUS 时须显式指定 -t sus（否则默认为 UGC）
```

</details>

### 2) 将本项目作为依赖库使用
#### 导入依赖库
- **推荐做法**：把本仓库作为 git submodule 引入你的工程仓库，然后把 `MuConvert.csproj` 加入你的 `.sln`/`.slnx`。

```shell
git submodule add https://github.com/MuNET-OSS/MuConvert MuConvert # 将本项目的源码导入为submodule
dotnet sln .\YourSolution.sln add .\MuConvert\MuConvert.csproj # 将项目加入解决方案
```

#### maimai转谱 - 使用方法（TLDR）：
> 以下 C# 示例中的 `Maidata`、`MaiChart`、`SimaiParser`、`MA2Parser`、`SimaiGenerator`、`MA2Generator` 等均位于命名空间 `MuConvert.mai`中，使用时需添加 `using MuConvert.mai;`。

**Simai → MA2**：
```csharp
string maidataText = File.ReadAllText(@"D:\charts\MyChart\maidata.txt", Encoding.UTF8); // maidata.txt 作为字符串
var maidata = new Maidata(maidataText); // 通过 Maidata 模块解析 maidata，得到整张谱的元信息，和每个难度的谱面
var inote = maidata.Levels[5].Inote; // 以紫谱为例，取出该难度的谱面内容（&inote_5）

var (chart, alerts) = new SimaiParser().Parse(inote); // 将 simai 解析为 MaiChart（谱面的表示对象）
var (ma2Text, alerts2) = new MA2Generator().Generate(chart); // 将 MaiChart 对象导出为 MA2 字符串
return ma2Text; // ma2Text即为转谱结果
```

**MA2 → Simai**：
```csharp
string ma2Text = File.ReadAllText(@"D:\charts\MyChart\000000_00.ma2", Encoding.UTF8); // MA2文件，整体读取为字符串
var (chart, alerts) = new MA2Parser().Parse(ma2Text); // 将 MA2 解析为 MaiChart（谱面的表示对象）。alerts 是解析时可能产生的警告信息等，建议打印出来。
var (simaiText, alerts2) = new SimaiGenerator().Generate(chart); // 将 MaiChart 导出为 Simai 文本

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

#### CHUNITHM转谱 - 使用方法（TLDR）：
> 以下 C# 示例中的各种Parser、Generator等，均位于命名空间 `MuConvert.chu`中，使用时需添加 `using MuConvert.chu;`。

```csharp
// 首先使用File.ReadAllText等方法，将谱面整体读取为字符串
var (c2sChart, alerts) = new C2sParser().Parse(c2sText); // 解析 C2S 谱面字符串
var (ugcChart, alerts) = new UgcParser().Parse(ugcText); // 解析 UGC 谱面字符串
var (susChart, alerts) = new SusParser().Parse(susText); // 解析 SUS 谱面字符串
// 以上得到的c2sChart、ugcChart、susChart，都是IChuChart类型的谱面表示对象；
// alerts是解析过程中可能产生的警告信息等，建议打印出来。

var (c2sText, alerts) = new C2sGenerator().Generate(ugcChart);   // UGC -> C2S
var (ugcText, alerts) = new UgcGenerator().Generate(c2sChart);   // C2S -> UGC
var (susText, alerts) = new SusGenerator().Generate(c2sChart);   // C2S -> SUS
// 各种Generator的Generate方法，均接受任意的IChuChart对象。
// 同上，alerts是生成过程中可能产生的警告信息等，建议打印出来。
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
using MuConvert.mai;
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
  - `SimaiParser.Parse(string)` → `MaiChart`
  - `MA2Parser.Parse(string)` → `MaiChart`
  - CHUNITHM的三种Parser(`C2sParser`、`UgcParser`、`SusParser`)：`Parse(string)` → `ChuChart`
  - 解析成功时，**返回值会同时带有 `List<Alert>`**，这是转谱过程中可能遇到的警告等信息，建议打印出来（直接对`Alert`对象`ToString()`即可）。
  - 如果解析失败，会抛出 `ConversionException`；该异常对象中同样含有一个 `List<Alert>`，是导致转谱失败的错误信息，可以同上打印出来。

- **中间表示 IR（Chart）**：MuConvert 内部统一的谱面数据结构
  - 对maimai，类型为 `MuConvert.mai.MaiChart`
  - 关键字段包括 `Chart.BpmList` 与 `Chart.Notes`，以及 `Touch/Hold/Slide` 等具体 `Note` 子类

- **generator（生成器）**：把中间表示转回“目标格式文本”
  - `SimaiGenerator.Generate(MaiChart)` → Simai 单谱文本（可写入 `maidata.txt` 的 `&inote_*`）
  - `MA2Generator.Generate(MaiChart)` → MA2 文本
  - CHUNITHM的三种Generator(`C2sGenerator`、`UgcGenerator`、`SusGenerator`)：`Generate(ChuChart)` → 目标格式的谱面文本
  - 与parser类似，成功生成时，**返回值会同时带有 `List<Alert>`**，这是转谱过程中可能遇到的警告等信息，建议打印出来（直接对`Alert`对象`ToString()`即可）。
  - 如果生成失败，会抛出 `ConversionException`；该异常对象中同样含有一个 `List<Alert>`，是导致转谱失败的错误信息，可以同上打印出来。


## 开发者指南
> 以下内容是面向对于本程序感兴趣，想要了解技术细节/调试bug/参与开发的开发者的。如果你只是普通用户，可以不必阅读以下内容；如果你遇到了bug，请通过[issue](https://github.com/MuNet-OSS/MuConvert/issues)进行反馈。

首先，推荐阅读：[Simai语言文档](https://w.atwiki.jp/simai/pages/1002.html)

### MuConvert的设计理念
- 转谱的本质就是transpiler。MuConvert严格遵循`源语言 ---parser--> 中间表示(IR) ---generator--> 目标语言`的transpiler通用设计模式，以确保代码的清晰和可维护性、减少冗余代码。
  - 以maimai为例，IR 在代码中就是`MaiChart`类，以及它所引用的`Note`、`BPMList`等类型。
- 然而，对MA2和Simai稍有了解的朋友们都知道，**它们二者的表达能力是不等价的**。（严格说来Simai的表达能力更强，但MA2也有一些独特的、没法简单的等价到Simai语法中的设计）
  - 最简单的一点是，MA2的所有音符都是对齐到Tick，即1/384小节的。你无法把类似`{36}1,1,1,1,`这样的、使用了无法被384整除的分音的Simai，转化为完全等价、不丢失任何信息的MA2格式，它在MA2中只能被近似到最接近的1/384分音上。
  - 此外还有一个重要的区别是，对于持续了一段时间的Hold或Slide、在其持续过程中BPM发生了变化的情况，MA2和Simai的定义也是完全不一样的。MA2在把“小节时间”计算为绝对时间时，会严格按照BPM表的声明、考虑BPM的变化；而Simai则被规定为仅按照音符开始时刻的BPM为基准，不考虑BPM的变化。详见下文[关于时间格式](#关于时间格式)部分所述。
- 因此，MuConvert的另一个设计目标是：在中间表示(IR)中不丢失任何信息。
  - 从源语言到IR的过程，即parser，确保是**无损**的、可以记录下来关于这个谱面的所有信息。这样可以有利于维护，也为将来的发展提供了更大的可扩展性。
  - 而在从IR到目标语言的过程，即 generator，则会根据目标语言的表达能力，进行必要的**近似**，所有的信息丢失都发生在generator中。

### 编译为单文件exe模式
> 这样就不用带着一堆依赖一起发给别人了，只发一个exe就行。
```shell
dotnet publish -c Release -r win-x64 -p:SelfContained=false -p:UseAppHost=true -p:PublishSingleFile=true
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
    - 因此，在`Duration`中会进一步地把底层的数据存储类型分为`Bar`和`InvariantBar`，对应于以上的两种情况。

### 关于parser的技术选型
- SimaiParser，考虑到Simai是一个相对复杂的格式化语法、本质是一种DSL，所以采用了[ANTLR](https://github.com/antlr/antlr4/blob/master/doc/index.md)进行解析。
  - ANTLR的核心是`g4`语法文件，因此在我们的代码里编写了针对Simai语言的语法定义文件：`Simai.g4`。如需修改，请自行学习ANTLR的文档。
  - ANTLR本身是不特定于编程语言的（它提供了各种编程语言的SDK），而语法文件的作用是，会被用于生成目标编程语言的“解析器代码”，以供调用。
    - 在`MuConvert.csproj`中定义了一个`<Antlr4>`的Item，它就是用来在编译时添加一个从语法文件生成C#解析器代码的编译步骤的。生成的文件会被自动放在`obj`目录下。
    - 具体的原理，请详见`parser/mai/SimaiParser.cs`中对`MuConvert.Antlr`下各生成类的引用。
- MA2的话，由于其天生就是为了机读设计的、格式相对简单，没有必要上ANTLR；而是直接逐行读取、一行内`Split('\t')`，就足以解析MA2的所有内容了。

### 目录与命名空间约定（贡献代码必读）
- 在目录上，本项目按**功能类型**划分一级子目录，如`parser`、`generator`、`chart`、`collection`等。
  - 在各个一级子目录中，如有必要的情况下，可再以**游戏名**创建二级子目录加以整理，如`parser/mai`、`generator/mai`、`chart/mai`等。
  - 这样可以使相同功能类型的代码相对集中，减少了重构时可能需要的工作量、防止重构漏掉东西等。
- 在命名空间上，本项目采用**按游戏划分**的逻辑命名空间，这样可以方便用户的使用。
  - **同一游戏下的谱面 IR、各语法格式的 Parser/Generator、以及其他直接相关的类型等**，一律放在统一的命名空间`MuConvert.<游戏简写>`下。
    - 例如，对maimai来说，尽管相关文件分散在`parser/mai`、`generator/mai`、`chart/mai`、`collection`等多个子目录中，但命名空间均为`MuConvert.mai`。
  - 多个游戏会共用的、**与具体游戏无关的基础设施**（例如谱面基类 `BaseChart<TNote>`、`IBaseChart`、`BPMList`、`IParser`等），则保留在 `MuConvert.chart`、`MuConvert.parser` 等目录级公共命名空间中。
  - 将来若接入其他游戏，应为其单独使用一个顶层命名空间（例如`MuConvert.chu`、`MuConvert.ogk`）。

### 多语言(i18n)相关
- 本项目中支持基于`System.Globalization`的多语言，语言文件位于`i18n`目录中。
- 其中，一级支持语言为三个（即[MaiChartManager](https://github.com/MuNET-OSS/MaiChartManager)支持的语言）：简体中文(`Locale.zh.resx`)、英语(`Locale.resx`)、繁体中文(`Locale.zh-hant.resx`)。
  - 一般的开发过程，包括有意提交PR的人在实现代码时需要新增/修改i18n key的，只需处理这三种语言文件即可。
- 其他的为二级支持语言，一般的开发过程可以不必处理和新增这些语言的翻译key；Maintainers会定期的将一级支持语言的翻译内容（通过LLM机器翻译）同步到这些语言中。
  - 当然，如果你发现翻译有错误，也可以直接提PR修改。

### 如何在已支持的游戏中新增一种语法格式
对于已经支持了的游戏，想要增加一种支持的源语法格式或目标语法格式时：  
**只需要编写Parser和Generator即可**。注意应当实现对应的接口(`IParser` / `IGenerator`)，详见下文。

1. **文件放置位置**：仿照现有的目录结构即可。以 maimai 为例，在 `parser/mai` 下新增 `FooParser`，在 `generator/mai` 下新增 `FooGenerator`。但是，如[目录与命名空间约定](#目录与命名空间约定贡献代码必读)部分所述，文件的命名空间应统一为`MuConvert.mai`。
2. **实现的接口**：应实现泛型参数为该游戏谱面类型的接口。继续以 maimai 为例，应实现的接口为 `IParser<MaiChart>`、`IGenerator<MaiChart>`。详见下方代码段的示例。
3. **Alerts**：所有的Parser和Generator，应当使用`utils/Error.cs`中定义的`Alert`类来实现转谱的信息的log，而不要往控制台上输出信息。
   - IParser和IGenerator的定义中都要求函数要返回List<Alert>，因此在你的解析/生成过程中，应当根据情况生成不同等级的Alert对象，并返回出来。而不是打印到控制台。
   - 如果遇到无法修复的错误、希望中止转谱过程的话，则应当（在记录好最后引发错误的Alert后），抛出`ConversionException`。
   - 具体写法，请参照现有的maimai的Parser和Generator等。

```csharp
using MuConvert.parser;
using MuConvert.utils;

namespace MuConvert.mai; // 注意命名空间应该是MuConvert.<游戏名>，不要使用默认的基于目录的命名空间

public sealed class FooParser : IParser<MaiChart>
{
    public (MaiChart, List<Alert>) Parse(string text)
    {
        var chart = new MaiChart();
        var alerts = new List<Alert>();
        if (text == "")
        { // 这是一个抛异常中止转谱的示例
            alerts.Add(new Alert(Alert.LEVEL.Error, "输入的文本为空！")); // 要把最后的错误记录成 Alert
            throw new ConversionException(alerts); // 然后抛出 ConversionException，把 alerts 传入。
        }
        // ……在此解析 text、写入 chart，并按需向 alerts 追加信息；遇不可恢复错误时同上抛出 ConversionException。
        return (chart, alerts);
    }
}
```

```csharp
using MuConvert.generator;
using MuConvert.utils;

namespace MuConvert.mai;

public sealed class FooGenerator : IGenerator<MaiChart>
{
    public (string, List<Alert>) Generate(MaiChart chart)
    {
        var alerts = new List<Alert>();
        // ……在此由 chart 生成目标文本，并按需向 alerts 追加信息。
        return ("", alerts);
    }
}
```

### 如何新增对另一种游戏的支持（注意事项）
1. **命名空间**：命名空间使用 `MuConvert.<游戏简写>`（如中二可用 `MuConvert.chu` 、音击可用 `MuConvert.ogk` ），
2. **代码目录**：模仿现有maimai的写法，在各个功能分类的一级子目录中新建二级子目录，例如 `chart/ogk`、`parser/ogk`、`generator/ogk`等，把代码放到这些目录下。
   - 特殊地，如果该游戏的某个功能类型非常简单，如只有一个文件，那么也可以不单独创建二级子目录，而是直接放在一级子目录下。例子见现有的`collection/Maidata.cs`，因为与maimai相关的collection只有这一个文件（MuConvert本身不对Sinmai的`Music.xml`做处理，这太复杂了、而且是MCM该干的事），所以`Maidata.cs`直接放在了`collection`一级目录下，没有区分二级子目录。
3. **中间表示（IR）**：为该游戏定义音符类型与谱面类型。
   - 音符类型，没有全局公共的基类，各个游戏自己定义即可。
   - 谱面类型，应继承`BaseChart`，并实现其中的抽象getter，同时添加上自己特定于自己这个游戏的属性和方法等。
4. **Parser & Generator**：详见上文[如何在已支持的游戏中新增一种语法格式](#如何在已支持的游戏中新增一种语法格式)部分的说明，编写Parser和Generator，并实现对应的接口。
5. **测试**：在`tests/`下新建你游戏的子目录（如`tests/ogk/`），在其中放置测试文件。直接放置即可，xUnit会自动找到，无需修改csproj等。
   - 谱面等测试数据可放在如`tests/ogk/testset/`中；读取路径时可参考`tests/mai/TestUtils.cs`中的`FindTestsetRoot()`函数的写法。
6. **多语言（i18n）**（可选）：如果有i18n的必要的话，直接在`i18n`目录中的对应的语言文件`.resx`中，新增对应的key即可。
   - 无需创建单独文件，也不用管命名空间之类的问题，但建议key在命名的时候遵循一定的规律以防冲突。
   - 详见上文[多语言(i18n)相关](#多语言i18n相关)部分的说明。
7. **CLI**（可选）：如需实现CLI，可在`Program.cs`中增加相应的功能。

