using System.Text.RegularExpressions;
using Antlr4.Runtime;
using MuConvert.Antlr;
using MuConvert.chart;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;
using P = MuConvert.Antlr.SimaiParser;

namespace MuConvert.parser.simai;

public class SimaiParser : SimaiBaseVisitor<object>, IParser
{
    private readonly Chart chart;
    private readonly List<Alert> alerts = [];

    private Rational now = 0;
    private Rational step = new(1, 4);

    private ParserRuleContext? currContext; // 供调试报错AddAlert函数使用
    private Note? currNote; // 用于在部分visitor之间传递额外的参数，如visitDuration、visitSlideBody等，都需要Note对象作为参数传入的情况
    private readonly List<string> extraModifiers = [];

    public SimaiParser(bool bigTouch = false, bool isUtage = false, int clockCount = 4)
    {
        chart = new Chart { DefaultTouchSize = bigTouch ? "L1" : "M1", IsUtage = isUtage, ClockCount = clockCount};
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
    
    public class AntlrErrorListener(SimaiParser parser) : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
    {
        // 这个是给 Parser 用的 (IToken)
        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {
            parser.alerts.Add(new Alert(Error, Locale.SimaiGrammarFailed + msg, line: line, relevantNote: e.Context?.GetText()));
            throw new ConversionException(parser.alerts, e);
        }

        // 这个是给 Lexer 用的 (int)
        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {
            parser.alerts.Add(new Alert(Error, Locale.SimaiGrammarFailed + msg, line: line, relevantNote: e.Context?.GetText()));
            throw new ConversionException(parser.alerts, e);
        }
    }

    public string Preprocess(string text)
    {
        // 移除注释
        var commentRegex = new Regex(@"((?<!\[[^\]]*|\{[^\}]*)#|\|\|).*$", RegexOptions.Multiline);
        text = commentRegex.Replace(text, "");
        
        // TODO 应用更多的纠错修改（从MCM现有代码里面抄）
        
        return text;
    }

    public (Chart, List<Alert>) Parse(string text)
    {
        if (now != 0) throw new Exception(Locale.InstanceMultipleUsage);
        P.ChartContext root;
        
        try
        {
            text = Preprocess(text);
            var inputStream = new AntlrInputStream(text);
            
            var errorListener = new AntlrErrorListener(this);
            var lexer = new SimaiLexer(inputStream);
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(errorListener);
            var tokens = new CommonTokenStream(lexer);
            
            var parser = new P(tokens); // MuConvert.Antlr.SimaiParser
            parser.RemoveErrorListeners();
            parser.AddErrorListener(errorListener);
            root = parser.chart();
            
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
        
        return (chart, alerts);
    }

    public sealed override object VisitChart(P.ChartContext context)
    {
        foreach (var notations in context.notations())
        {
            VisitNotations(notations);
            now = (now + step).CanonicalForm;
        }
        return true;
    }

    public sealed override object VisitNotations(P.NotationsContext context)
    { // 形如 (120){4}1/1 算作一组notations
        foreach (var child in context.children ?? [])
        {
            if (child is P.BpmTagContext bpmTag)
            {
                VisitBpmTag(bpmTag);
            }
            else if (child is P.AbsulouteStepTagContext absoluteStepTag)
            {
                VisitAbsulouteStepTag(absoluteStepTag);
            }
            else if (child is P.MetTagContext metTag)
            {
                VisitMetTag(metTag);
            }
            else if (child is P.NoteGroupContext noteGroup)
            {
                VisitNoteGroup(noteGroup);
            }
        }
        return true;
    }

    public sealed override object VisitAbsulouteStepTag(P.AbsulouteStepTagContext context)
    {
        AddAlert(Error, Locale.AbsoluteStepNotImplemented, context);
        throw new ConversionException(alerts);
    }

    public sealed override object VisitBpmTag(P.BpmTagContext context)
    {
        currContext = context;
        var bpm = (decimal)VisitNumber(context.number());
        chart.BpmList.Add(new BPM(now, bpm));
        return true;
    }

    public sealed override object VisitMetTag(P.MetTagContext context)
    { // metTag指的是标记分音的tag，如{4}
        currContext = context;
        var quaver = int.Parse(context.@int().GetText());
        step = new Rational(1, quaver);
        return true;
    }

    public sealed override object VisitNumber(P.NumberContext context)
    {
        return decimal.Parse(context.GetText());
    }

    public sealed override object VisitNoteGroup(P.NoteGroupContext context)
    { // 同一时刻出现的（双押，伪双押，同头星星...）构成一个NoteGroup。例如"1/2`3/4"，`1-2*-3[2:1]/4-5*-6[4:1]`都是NoteGroup。
        currContext = context;
        int falseEachIdx = 0;
        foreach (var child in context.children)
        {
            P.NoteContext noteC;
            if (child is P.NoteContext c1) noteC = c1;
            else if (child is P.EachNoteContext c2) noteC = c2.note();
            else if (child is P.FalseEachNoteContext c3)
            {
                noteC = c3.note();
                falseEachIdx++;
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
        currContext = context;
        List<Note> result = [];
        foreach (var child in context.children)
        {
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
                default:
                    throw Utils.Fail();
            }
            result.Add(note);

            if (extraModifiers.Count > 0)
            {
                AddAlert(Warning, string.Format(Locale.ExtraModifiersIgnored, string.Join("", extraModifiers)), (ParserRuleContext)child);
            }
        }
        return result;
    }

    private void ApplyModifiers(P.ModifiersContext[] modifiersList, Note note)
    { // 提取可能在不同位置出现的所有modifiers
      // 将通用的modifier(即b和x)应用到note上，其余的modifier则通过extraModifiers数组返回。
        HashSet<string> set = new();
        foreach (var modifiers in modifiersList)
        {
            foreach (var modifier in modifiers.children ?? [])
            {
                set.Add(modifier.GetText());
            }
        }

        extraModifiers.Clear();
        foreach (var k in set)
        {
            if (k == "b") note.IsBreak = true;
            else if (k == "x") note.IsEx = true;
            else extraModifiers.Add(k);
        }
    }

    public sealed override object VisitTap(P.TapContext context)
    {
        currContext = context;
        var result = new Tap(chart, now)
        {
            Key = int.Parse(context.KEY().GetText())
        };
        ApplyModifiers([context.modifiers()], result);
        if (extraModifiers.Remove("$$") || extraModifiers.Remove("$"))
        { // 发现了”TAP_TO_STAR“的标记，把Tap转换为星星
            result = new Star(result);
        }
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
        if (extraModifiers.Remove("f")) result.IsFirework = true;
        return result;
    }

    public sealed override object VisitDuration(P.DurationContext context)
    {
        var result = new Duration(currNote!);
        if (context.beats() != null) result.InvariantBar = (Rational)VisitBeats(context.beats());
        else result.Seconds = (Rational)(decimal)VisitNumber(context.number());        
        return result;
    }

    public sealed override object VisitBeats(P.BeatsContext context)
    {
        return new Rational(int.Parse(context.children[2].GetText()), int.Parse(context.children[0].GetText()));
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
        if (extraModifiers.Remove("f")) result.IsFirework = true;
        return result;
    }
    
    public sealed override object VisitSlideDuration(P.SlideDurationContext context)
    {
        var result = new Duration(currNote!);
        Duration? waitTime = null;

        if (context.waitTime() != null)
        {
            waitTime = new Duration(currNote!)
            {
                Seconds = (Rational)(decimal)VisitNumber(context.waitTime().number())
            };
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
                waitTime ??= new Duration(currNote!)
                {
                    Seconds = 60 / bpm
                };
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
                Type = SlideTypeTool.FromSimai(context.slideType()[i].GetText(), slide.EndKey), // 在新的segment被添加之前，此前的slide部分的EndKey就是新segment的StartKey
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
            var waitTimeSet = false;
            for (int i = 0; i < durationCount; i++)
            {
                var C = context.slideDuration()[i];
                var (waitTime, duration) = ((Duration?, Duration))VisitSlideDuration(C);
                if (waitTime != null)
                {
                    if (waitTimeSet || (i > 0 && i < durationCount - 1))
                    {
                        AddAlert(Warning, Locale.InvalidWaitTime);
                    }
                    else
                    {
                        slide.WaitTime = waitTime;
                        waitTimeSet = true;
                    }
                }
                slide.segments[i].Duration = duration;
            }
        }
        else throw Utils.Fail("duration的个数不对"); // 已经在语法层做过检查了，所以这个分支按说是永远不会命中的。

        ApplyModifiers(context.modifiers(), slide);
        return true;
    }

    public sealed override object VisitSlide(P.SlideContext context)
    {
        currContext = context;
        var result = new Slide(chart, now);
        
        // 处理星星头
        Tap? head = (Tap)VisitTap(context.tap());
        if (context.NO_STAR() != null)
        { // 标记了NO_STAR的星星，则不要放head、但是需要手动设置Key
            result.Key = head.Key;
            head = null;
        }
        else if (context.STAR_TO_TAP() == null) head = new Star(head); // 除非标记了STAR_TO_TAP，否则把tap转为star
        result.Head = head;
        
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