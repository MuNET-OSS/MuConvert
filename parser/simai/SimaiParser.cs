using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using MuConvert.Antlr;
using MuConvert.chart;
using MuConvert.parser.simai;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;
using P = MuConvert.Antlr.SimaiParser;
using L = MuConvert.Antlr.SimaiLexer;
using Utils = MuConvert.utils.Utils;

namespace MuConvert.parser;

public partial class SimaiParser : SimaiBaseVisitor<object>, IParser
{
    /**
     * 表示parser工作的严格程度的枚举。默认为Normal。
     * 
     * Strict: 不允许任何语法错误，任何语法错误直接报错。
     * Normal: 允许进行一些小的修复，如删除确定多出的符号、补充确定残缺的符号等。但遇到大的错误会直接解析失败。
     * Lax: 尽全力解析，出现错误的音符直接吞掉不解析，以换取整个谱面不要解析失败。
     */
    public enum StrictLevelEnum { Strict, Normal, Lax }
    public StrictLevelEnum StrictLevel;

    internal readonly Chart chart;
    internal readonly List<Alert> alerts = [];

    private Rational now = 0;
    private Rational step = new(1, 4);
    private decimal? absoluteTimeStep; // 此项必须和step本体一起更改
    private Rational extendedFalseEach = 0; // 扩展伪双押语法（多个连续的`）累计后移了多少时间。每次遇到逗号时，这个数字需要清零。

    private ParserRuleContext? currContext; // 供调试报错AddAlert函数使用
    private Note? currNote; // 用于在部分visitor之间传递额外的参数，如visitDuration、visitSlideBody等，都需要Note对象作为参数传入的情况
    private bool isRealExactWaitTime; // 用于在VisitSlideBody和VisitSlideDuration之间传递额外的参数
    private readonly List<IToken> extraModifiers = []; // 通过ApplyModifiers不能通用处理的modifiers，需要落回到具体的音符处理逻辑中进行处理的。
    
    private bool absoluteTimeStepWarned; // 用于确保Warning只打印一次
    private bool extendedFalseEachWarned;

    public SimaiParser(bool bigTouch = false, int clockCount = 4, StrictLevelEnum strictLevel = StrictLevelEnum.Normal)
    {
        chart = new Chart { DefaultTouchSize = bigTouch ? "L1" : "M1", ClockCount = clockCount};
        StrictLevel = strictLevel;
    }
    
    private void AddAlert(Alert.LEVEL level, string content, ParserRuleContext? context = null)
    {
        var alert = new Alert(level, content, barTime: (chart, now));
        context ??= currContext;
        if (context != null)
        {
            alert.Line = context.Start.Line;
            alert.RelevantNote = context.GetText();
        }
        alerts.Add(alert);
    }
    
    [GeneratedRegex(@"(?<!\[[^\]]*|\{[^\}]*)#.*$", RegexOptions.Multiline)]
    private static partial Regex InlineSharpCommentRegex(); // 这里仅处理#开头的注释，因为||开头的注释在语法文件里已经处理过了。

    private string Preprocess(string text)
    {
        // 移除注释
        text = InlineSharpCommentRegex().Replace(text, "");

        return text;
    }

    /**
     * 对词法分析得到的token流，在送入parser之前进行一些处理，以尝试修复一些特定类型的错误：
     * - 对星星头的修饰符，应该出现在键位号后、星星类型标记之前
     */
    private CommonTokenStream TokenProcess(CommonTokenStream src)
    {
        List<Alert> alertsBuf = [];
        
        src.Fill();
        var tokens = src.GetTokens().Index().Where(x=>x.Item.Channel == TokenConstants.DefaultChannel).ToList();
        var r = new TokenStreamRewriter(src);
        bool modified = false;
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            var (idx, token) = tokens[i];
            if (token.Type == L.SLIDE_TYPE && 
                (i < tokens.Count - 1 && Utils.IsModifier(tokens[i+1].Item.Type)) && // SlideType后面接了modifier
                (i >= 2 && tokens[i-1].Item.Type == L.KEY && tokens[i-2].Item.Type != L.SLIDE_TYPE)) // 判断是否是星星的首个slidetype
            { // 类似1-b2[2:1]这种，星星头的修饰符错误地出现在了首个slidetype后面的情况。
                // 找到modifier的结束位置
                int endPos = i+1;
                while (Utils.IsModifier(tokens[endPos + 1].Item.Type)) endPos++;
                // 将tokens[i]挪到endPos后面去
                r.Delete(idx);
                r.InsertAfter(tokens[endPos].Index, token.Text);
                alertsBuf.Add(new Alert(Warning, Locale.FixModifiersOnHead + Locale.Fixed, line: token.Line, 
                    relevantNote: src.GetText(tokens[i-1].Item, tokens[endPos + 1].Item)));
                modified = true;
            }
        }

        if (!modified) return src;  
        // 做过更改，则要重跑lexer
        alerts.Clear(); // 清空上次跑lexer时的报错，避免重复报错
        alerts.AddRange(alertsBuf);
        var inputStream = new AntlrInputStream(r.GetText());
        var lexer = new SimaiLexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new ErrorListener(this));
        return new CommonTokenStream(lexer);
    }

    public (Chart, List<Alert>) Parse(string text)
    {
        if (now != 0) throw new Exception(Locale.InstanceMultipleUsage);
        P.ChartContext root;
        
        text = Preprocess(text); // 预处理
        
        try
        { // 词语法分析
            var inputStream = new AntlrInputStream(text);
            var lexer = new SimaiLexer(inputStream);
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(new ErrorListener(this));
            var tokens = new CommonTokenStream(lexer);
            if (StrictLevel != StrictLevelEnum.Strict) tokens = TokenProcess(tokens);
            
            var parser = new P(tokens) { ErrorHandler = ErrorStrategy() }; // MuConvert.Antlr.SimaiParser
            parser.RemoveErrorListeners();
            parser.AddErrorListener(new ErrorListener(this));
            root = parser.chart();
            if (root.children.Count == 1)
            { // 只有一个EOF
                alerts.Add(new Alert(Error, Locale.NoNotesInChart)); 
                throw new ConversionException(alerts);
            }
        }
        catch (ParseCanceledException e)
        { // ErrorListener里会把alerts加好的，因此这里直接抛异常就可以了。
            throw new ConversionException(alerts, e);
        }
        
        try
        { // 基于语法分析树，进行具体的解析和遍历
            VisitChart(root);
        }
        catch (ConversionException)
        {
            throw; // 看到主动丢出的ConversionException，就说明错误信息已经被加到message中过了。直接丢回去即可。
        }
        catch (Exception e)
        {
            // 否则，说明是意外的Exception，把它附加上详细信息、转换为一般的Exception。
            AddAlert(Error, e.Message);
            throw new ConversionException(alerts, e);
        }
        
        chart.Sort();
        return (chart, alerts);
    }

    private IAntlrErrorStrategy ErrorStrategy()
    {
        switch (StrictLevel)
        {
            case StrictLevelEnum.Strict:
                return new BailErrorStrategy();
            case StrictLevelEnum.Lax:
                return new LaxErrorStrategy(this);
            case StrictLevelEnum.Normal:
            default:
                return new ModerateErrorStrategy(this);
        }
    }

    public sealed override object VisitChart(P.ChartContext context)
    {
        foreach (var notations in context.notations())
        {
            VisitNotations(notations);
            if (chart.BpmList.Count == 0) AddDefaultBpm();
            if (extendedFalseEach > 0)
            { // 如果之前的解析过程中，触发了extendedFalseEach的话。则要把被额外增加的时间扣回来。
                now -= extendedFalseEach;
                extendedFalseEach = 0;
            }
            now = (now + step).CanonicalForm;
        }
        return true;
    }

    private void AddDefaultBpm()
    {
        // 谱面开头还没看到BPM，就看到音符（或绝对时间标记）了。这在MA2中是不允许的，MA2的BPM必须是从一开头就开始指定。
        // 因此，我们打印一个警告，然后帮用户补一个60。
        const int defaultStartBpm = 60;
        AddAlert(Warning, string.Format(Locale.StartNoBpm, defaultStartBpm));
        Utils.Assert(now == 0, "现在已经不是开头了？？");
        chart.BpmList.Add(new BPM(now, defaultStartBpm));
    }

    private static bool SubtreeHasException(ParserRuleContext root)
    {
        if (root.exception != null) return true;
        foreach (var child in root.children ?? Array.Empty<IParseTree>())
        {
            if (child is ParserRuleContext pr && SubtreeHasException(pr)) return true;
        }
        return false;
    }

    public sealed override object VisitNotations(P.NotationsContext context)
    { // 形如 (120){4}1/1 算作一组notations
        foreach (var child in context.children ?? [])
        {
            if (child is IErrorNode) continue; // 忽略错误节点
            if (child is P.BpmTagContext bpmTag)
            {
                VisitBpmTag(bpmTag);
            }
            else if (child is P.MetTagContext metTag)
            {
                VisitMetTag(metTag);
            }
            else
            {
                if (chart.BpmList.Count == 0) AddDefaultBpm();
                if (child is P.AbsulouteStepTagContext absoluteStepTag)
                {
                    VisitAbsulouteStepTag(absoluteStepTag);
                }
                else if (child is P.NoteGroupContext noteGroup)
                {
                    VisitNoteGroup(noteGroup);
                }
            }
        }
        return true;
    }

    private void WarnMoreThanOneTokens(IList<IToken> ps)
    {
        if (ps.Count <= 1) return;
        var extraStr = "'" + string.Join("", ps.Skip(1).Select(x => x.Text)) + "'";
        if (StrictLevel == StrictLevelEnum.Strict)
        { // 严格模式，抛异常
            AddAlert(Error, string.Format(Locale.RecoverInlineExtraneousTokenStrict, extraStr));
            throw new ConversionException(alerts);
        }
        else AddAlert(Warning, string.Format(Locale.RecoverInlineExtraneousToken, extraStr));
    }
    
    private void WarnMoreParentheses(IList<IToken> lp, IList<IToken> rp)
    {
        WarnMoreThanOneTokens(lp);
        WarnMoreThanOneTokens(rp);
    }

    public sealed override object VisitAbsulouteStepTag(P.AbsulouteStepTagContext context)
    {
        if (SubtreeHasException(context)) return false; // 如果本节点下有异常，则直接整个吞掉，（避免具体的规则遇到不完整子树、爆出更不可预测的错误）
        if (!absoluteTimeStepWarned)
        {
            AddAlert(Warning, string.Format(Locale.AbsoluteStepUsed, context.GetText()), context);
            absoluteTimeStepWarned = true;
        }
        currContext = context;
        WarnMoreParentheses(context._lp, context._rp);
        absoluteTimeStep = (decimal)VisitNumber(context.number());
        var currentBpm = chart.BpmList.Last().Bpm;
        step = (Rational)absoluteTimeStep / (240 / (Rational)currentBpm);
        return true;
    }

    public sealed override object VisitBpmTag(P.BpmTagContext context)
    {
        if (SubtreeHasException(context)) return false; // 如果本节点下有异常，则直接整个吞掉，（避免具体的规则遇到不完整子树、爆出更不可预测的错误）
        currContext = context;
        WarnMoreParentheses(context._lp, context._rp);
        var bpm = (decimal)VisitNumber(context.number());
        chart.BpmList.Add(new BPM(now, bpm));
        if (absoluteTimeStep != null)
        { // 如果当前处于绝对时间step模式下，则bpm变化也会引起小节制step的变化，更新之。
            step = (Rational)absoluteTimeStep / (240 / (Rational)bpm);
        }
        return true;
    }

    public sealed override object VisitMetTag(P.MetTagContext context)
    { // metTag指的是标记分音的tag，如{4}
        if (SubtreeHasException(context)) return false; // 如果本节点下有异常，则直接整个吞掉，（避免具体的规则遇到不完整子树、爆出更不可预测的错误）
        currContext = context;
        WarnMoreParentheses(context._lp, context._rp);
        var quaver = int.Parse(context.@int().GetText());
        step = new Rational(1, quaver);
        absoluteTimeStep = null;
        return true;
    }

    public sealed override object VisitNoteGroup(P.NoteGroupContext context)
    { // 同一时刻出现的（双押，伪双押，同头星星...）构成一个NoteGroup。例如"1/2`3/4"，`1-2*-3[2:1]/4-5*-6[4:1]`都是NoteGroup。
        currContext = context;
        int falseEachIdx = 0;
        foreach (var child in context.children)
        {
            if (child is IErrorNode) continue; // 忽略错误节点
            P.NoteContext noteC;
            if (child is P.NoteContext c1) noteC = c1;
            else if (child is P.EachNoteContext c2)
            {
                noteC = c2.note();
                
                var separators = c2._sep;
                if (separators.Count >= 2 && separators.All(x=>x.Type == L.FALSE_EACH))
                {
                    // 出现连续多个反引号的情况，如"2``3"。
                    // 这并不是标准的simai语法。但是，MajdataView中对此提供了支持，将每个`实现为128分音。
                    // 因此，我们也支持这一特性，在遇到大于一个`时，不实现成FalseEachIndex，而是直接给予相同的实现、每个`错后128分音。
                    var length = separators.Count * new Rational(1, 128);
                    now = (now + length).CanonicalForm;
                    extendedFalseEach += length;
                    falseEachIdx = 0;
                    if (!extendedFalseEachWarned)
                    {
                        AddAlert(Warning, Locale.ExtenedFalseEach, context);
                        extendedFalseEachWarned = true;
                    }
                }
                else
                {
                    WarnMoreThanOneTokens(separators);
                    if (separators[0].Type == L.FALSE_EACH) falseEachIdx++;
                }
            }
            else throw Utils.Fail();

            var result = (List<Note>)VisitNote(noteC);
            foreach (var note in result)
            {
                note.FalseEachIdx = falseEachIdx;
                chart.Notes.Add(note);
            }
        }
        return true;
    }

    public sealed override object VisitNote(P.NoteContext context)
    { 
        // 注：这个函数返回的是List<Note>，因为ANTLR中的NoteContext，虽然大多数时候只对应一个Note，但有时也可能是两个以上！
        // 具体而言，两种情况：1. "1234"这种simai允许的tap多押简略记法（等价于"1/2/3/4"）
        // 2. 同头星星如"1-2[2:1]*-3[2:1]"，它在我们定义的ANTLR语法中是作为一个note节点的！
        if (SubtreeHasException(context)) return new List<Note>(); // 如果本节点下有异常，则直接整个吞掉，（避免具体的规则遇到不完整子树、爆出更不可预测的错误）
        currContext = context;
        List<Note> result = [];
        foreach (var child in context.children)
        {
            if (child is IErrorNode) continue;
            Note note;
            switch (child)
            {
                case P.TapContext tapC:
                    note = (Tap)VisitTap(tapC);
                    break;
                case P.HoldContext holdC:
                    note = (Hold)VisitHold(holdC);
                    break;
                case P.TouchContext touchC:
                    note = (Touch)VisitTouch(touchC);
                    break;
                case P.TouchHoldContext touchHoldC:
                    note = (TouchHold)VisitTouchHold(touchHoldC);
                    break;
                case P.SlideContext slideC:
                    note = (Slide)VisitSlide(slideC);
                    break;
                case P.SharedHeadSlideContext shSlideC:
                    note = (Slide)VisitSharedHeadSlide(shSlideC);
                    break;
                case ITerminalNode n when n.Symbol.Type == L.KEY:
                    note = new Tap(chart, now) { Key = int.Parse(n.GetText())};
                    break;
                default:
                    throw Utils.Fail();
            }
            result.Add(note);

            if (extraModifiers.Count > 0)
            {
                AddAlert(Warning, string.Format(Locale.ExtraModifiersIgnored, string.Join("", extraModifiers.Select(x=>x.Text))), (ParserRuleContext)child);
            }
        }
        return result;
    }
    
    public sealed override object VisitNumber(P.NumberContext context)
    {
        return decimal.Parse(context.GetText());
    }

    private void ApplyModifiers(P.ModifiersContext[] modifiersList, Note note, bool clearExtraArr = true)
    { // 提取可能在不同位置出现的所有modifiers
      // 将通用的modifier(即b和x)应用到note上，其余的modifier则通过extraModifiers数组返回。
        if (clearExtraArr) extraModifiers.Clear();
        foreach (var modifiers in modifiersList)
        {
            foreach (var child in modifiers.children ?? [])
            {
                if (child is IErrorNode) continue;
                if (child is not ITerminalNode modifier) throw Utils.Fail("modifiers里面居然不是ITerminalNode");
                var token = modifier.Symbol;
                if (token.Text == "b" && note is not Touch) note.IsBreak = true;
                else if (token.Text == "x" && note is Tap) note.IsEx = true;
                else if (token.Text == "f" && note is Touch touch) touch.IsFirework = true;
                else extraModifiers.Add(token);
            }
        }
    }

    private bool GetModifier(int tokenType)
    {
        var idx = extraModifiers.FindIndex(x => x.Type == tokenType);
        if (idx == -1) return false;
        extraModifiers.RemoveAt(idx);
        return true;
    }

    public sealed override object VisitTap(P.TapContext context)
    {
        currContext = context;
        var result = new Tap(chart, now)
        {
            Key = int.Parse(context.KEY().GetText())
        };
        ApplyModifiers([context.modifiers()], result);
        if (context.Parent is not P.SlideContext && GetModifier(L.TAP_TO_STAR)) result = new Star(result); // 发现了”TAP_TO_STAR“的标记，把Tap转换为星星
        return result;
    }

    public sealed override object VisitTouch(P.TouchContext context)
    {
        currContext = context;
        var result = new Touch(chart, now)
        {
            TouchArea = context.TOUCH_AREA().GetText()
        };
        ApplyModifiers([context.modifiers()], result);
        return result;
    }

    public sealed override object VisitDuration(P.DurationContext? context)
    {
        var result = new Duration(currNote!);
        if (context == null)
        { // context为null，说明hold上没有写持续时间标记。根据文档，这属于“疑似each”，持续时间定义为0。
            result.InvariantBar = 0;
            return result;
        }
        WarnMoreParentheses(context._lp, context._rp);
        if (context.beats() != null) result.InvariantBar = (Rational)VisitBeats(context.beats());
        else result.Seconds = (Rational)(decimal)VisitNumber(context.number());        
        return result;
    }

    public sealed override object VisitBeats(P.BeatsContext context)
    {
        return new Rational(int.Parse(context.@int(1).GetText()), int.Parse(context.@int(0).GetText()));
    }

    public sealed override object VisitHold(P.HoldContext context)
    {
        currContext = context;
        var result = new Hold(chart, now)
        {
            Key = int.Parse(context.KEY().GetText())
        };
        currNote = result;
        var duration = (Duration)VisitDuration(context.duration());
        result.Duration = duration;

        ApplyModifiers(context.modifiers(), result);
        return result;
    }
    
    public sealed override object VisitTouchHold(P.TouchHoldContext context)
    {
        currContext = context;
        var result = new TouchHold(chart, now)
        {
            TouchArea = context.TOUCH_AREA().GetText()
        };
        currNote = result;
        var duration = (Duration)VisitDuration(context.duration());
        result.Duration = duration;
        
        ApplyModifiers(context.modifiers(), result);
        return result;
    }
    
    public sealed override object VisitSlideDuration(P.SlideDurationContext context)
    {
        var result = new Duration(currNote!);
        Duration? waitTime = null;
        isRealExactWaitTime = false;
        WarnMoreParentheses(context._lp, context._rp);

        if (context.waitTime() != null)
        {
            waitTime = new Duration(currNote!)
            {
                Seconds = (Rational)(decimal)VisitNumber(context.waitTime().number())
            };
            isRealExactWaitTime = true;
        }
        if (context.number() != null) result.Seconds = (Rational)(decimal)VisitNumber(context.number());
        else
        {
            var value = (Rational)VisitBeats(context.beats());
            if (context.asBpm() == null) result.InvariantBar = value;
            else
            {
                // 根据强行指定的bpm换算为秒数
                var bpm = (Rational)(decimal)VisitNumber(context.asBpm().number());
                result.Seconds = value * (240 / bpm);
                // 如果未显式指定waitTime，则waitTime也要变成强行指定的bpm下的一拍。不然就是音符所在时刻下的一拍了。
                waitTime ??= new Duration(currNote!) { Seconds = 60 / bpm };
            }
        }
        return (waitTime, result);
    }

    public sealed override object VisitSlideBody(P.SlideBodyContext context)
    {
        var slide = (Slide)currNote!;
        
        Utils.Assert(context.slideType().Length == context.KEY().Length);
        for (int i = 0; i < context.slideType().Length; i++)
        {
            var key = int.Parse(context.KEY()[i].GetText());
            var segment = new SlideSegment((Slide)currNote!)
            {
                Type = SlideTypeTool.FromSimai(context.slideType()[i].GetText(), slide.EndKey, key), // 在新的segment被添加之前，此前的slide部分的EndKey就是新segment的StartKey
                EndKey = key
            };
            slide.segments.Add(segment);
        }
        
        // 接下来开始添加时间
        var durationCount = context.slideDuration().Length;
        if (durationCount == 1)
        { // 第一种情况，只有一个时间标记。则是全局时间标记
            var C = context.slideDuration()[0];
            var (waitTime, duration) = ((Duration?, Duration))VisitSlideDuration(C);
            if (waitTime != null) slide.WaitTime = waitTime;
            slide.Duration = duration;
        }
        else if (durationCount == context.slideType().Length)
        { // 第二种情况，每个上都有时间标记
            var waitTimeSet = 0; // 0:waitTime还未被设置，1:waitTime已被隐式设置，2:waitTime已被显式设置
            for (int i = 0; i < durationCount; i++)
            {
                var C = context.slideDuration()[i];
                var (waitTime, duration) = ((Duration?, Duration))VisitSlideDuration(C);
                if (waitTime != null)
                {
                    // 本次返回的waitTime的强度。用户显式设置的记为2，用户未显式设置、但是中括号中形如[190#8:3]这样指定了bpm、导致产生了一个隐式的waitTime的，记为1。
                    var hereStrength = isRealExactWaitTime ? 2 : 1;
                    // 采信这个waitTime的条件：必须在头尾，且强度更大
                    if ((i == 0 || i == durationCount - 1) && hereStrength > waitTimeSet)
                    {
                        slide.WaitTime = waitTime;
                        waitTimeSet = hereStrength;
                    }
                    // 给警告的条件：未被采信（即上一个分支没命中），且是显式设置的
                    else if (isRealExactWaitTime) AddAlert(Warning, Locale.InvalidWaitTime);
                }
                slide.segments[i].Duration = duration;
            }
        }
        else throw Utils.Fail("duration的个数不对"); // 已经在语法层做过检查了，所以这个分支按说是永远不会命中的。

        ApplyModifiers(context.modifiers(), slide, clearExtraArr: false);
        return true;
    }

    public sealed override object VisitSlide(P.SlideContext context)
    {
        currContext = context;
        var result = new Slide(chart, now);
        
        // 处理星星头
        Tap? head = (Tap)VisitTap(context.tap());
        if (GetModifier(L.NO_STAR))
        { // 标记了NO_STAR的星星，则不要放head、但是需要手动设置Key
            result.Key = head.Key;
            head = null;
        }
        else if (!GetModifier(L.STAR_TO_TAP)) head = new Star(head); // 除非标记了STAR_TO_TAP，否则把tap转为star
        result.OwnHead = head;
        
        currNote = result;
        VisitSlideBody(context.slideBody());
        return result;
    }

    public sealed override object VisitSharedHeadSlide(P.SharedHeadSlideContext context)
    {
        currContext = context;
        var result = new Slide(chart, now);
        if (currNote is Slide prevSlide)
        {
            result.SharedHeadWith = prevSlide.SharedHeadWith??prevSlide;
        }
        else throw Utils.Fail("同头星星，找不到上一条");
        
        currNote = result;
        VisitSlideBody(context.slideBody());
        return result;
    }
}