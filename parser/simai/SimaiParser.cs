using Antlr4.Runtime;
using MuConvert.Antlr;
using MuConvert.chart;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Message.LEVEL;
using P = MuConvert.Antlr.SimaiParser;

namespace MuConvert.parser.simai;

public class SimaiParser : SimaiBaseVisitor<object>, IParser
{
#pragma warning disable CS8618
    private Chart chart;
#pragma warning restore CS8618
    private List<Message> messages = [];

    private Rational now;
    private Rational step;

    private ParserRuleContext? currContext;
    
    private void AddMsg(Message.LEVEL level, string content, ParserRuleContext? context = null)
    {
        var message = new Message(level, content, barTime: (chart, now));
        context ??= currContext;
        if (context != null)
        {
            message.Line = context.Start.Line;
            message.RelevantNote = context.GetText();
        }
        messages.Add(message);
    }
    
    public (Chart, List<Message>) Parse(string text)
    {
        return Parse(text, false);
    }

    public (Chart, List<Message>) Parse(string text, bool bigTouch)
    {
        chart = new Chart { DefaultTouchSize = bigTouch ? "L1" : "M1"};
        now = 0;
        P.ChartContext root;

        try
        {
            var inputStream = new AntlrInputStream(text);
            var lexer = new SimaiLexer(inputStream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new P(tokens); // MuConvert.Antlr.SimaiParser
            root = parser.chart();
        }
        catch (RecognitionException e)
        {
            messages.Add(new Message(Error, Locale.SimaiGrammarFailed + e.Message, line: e.OffendingToken.Line, relevantNote: e.Context.GetText()));
            throw new ParsingException(messages);
        }
        catch (Exception e)
        {
            messages.Add(new Message(Error, Locale.SimaiGrammarFailed + e.Message));
            throw new ParsingException(messages);
        }
        
        try
        {
            VisitChart(root);
        }
        catch (ParsingException)
        {
            throw; // 看到主动丢出的ParsingException，就说明错误信息已经被加到message中过了。直接丢回去即可。
        }
        catch (Exception e)
        {
            // 否则，说明是意外的Exception，把它附加上详细信息、转换为一般的Exception。
            AddMsg(Error, e.Message);
            throw new ParsingException(messages);
        }
        
        return (chart, messages);
    }

    public sealed override object VisitChart(P.ChartContext context)
    {
        foreach (var notations in context.notations())
        {
            VisitNotations(notations);
            now += step;
        }
        return true;
    }

    public sealed override object VisitNotations(P.NotationsContext context)
    {
        foreach (var child in context.children)
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
                var result = VisitNoteGroup(noteGroup);
            }
        }
        return true;
    }

    public sealed override object VisitAbsulouteStepTag(P.AbsulouteStepTagContext context)
    {
        AddMsg(Error, Locale.AbsoluteStepNotImplemented, context);
        throw new ParsingException(messages);
    }

    public sealed override object VisitBpmTag(P.BpmTagContext context)
    {
        currContext = context;
        var bpm = (float)VisitNumber(context.number());
        chart.BpmList.Add(new BPM(now, bpm));
        return true;
    }

    public sealed override object VisitMetTag(P.MetTagContext context)
    {
        currContext = context;
        var quaver = int.Parse(context.INT().GetText());
        step = new Rational(1) / quaver;
        return true;
    }

    public sealed override object VisitNumber(P.NumberContext context)
    {
        return decimal.Parse(context.GetText());
    }
}