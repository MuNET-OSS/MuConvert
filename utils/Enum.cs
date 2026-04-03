namespace MuConvert.utils;

public enum TouchSeries
{
    A, B, C, D, E,
}

public enum SlideType
{
    SI_, // -
    SV_, // v（过中心）
    SCL, // 逆时针画大外圈（在simai中是 < 还是 > 取决于键位）
    SCR, // 顺时针画大外圈
    SUL, // p（逆时针画B区圈）
    SUR, // q（顺时针画B区圈）
    SXL, // pp
    SXR, // qq
    SLL, // V（首个点是逆时针找2个点）
    SLR, // V（首个点是顺时针找2个点）
    SSL, // s（逆时针方向开始）
    SSR, // z（顺时针方向开始）
    SF_, // w（wifi）
}

public static class SlideTypeTool
{
    public static string ToSimai(this SlideType type, int startKey)
    {
        switch (type)
        {
            case SlideType.SI_: return "-";
            case SlideType.SV_: return "v";
            case SlideType.SCL:
                return startKey is >= 3 and <= 6 ? ">" : "<";
            case SlideType.SCR:
                return startKey is >= 3 and <= 6 ? "<" : ">";
            case SlideType.SUL: return "p";
            case SlideType.SUR: return "q";
            case SlideType.SXL: return "pp";
            case SlideType.SXR: return "qq";
            case SlideType.SLL:
                return $"V{(startKey - 2 + 7) % 8 + 1}";
            case SlideType.SLR:
                return $"V{(startKey + 1) % 8 + 1}";
            case SlideType.SSL: return "s";
            case SlideType.SSR: return "z";
            case SlideType.SF_: return "w";
        }

        throw Utils.Fail();
    }

    public static SlideType FromSimai(string s, int? startKey)
    {
        if (s[0] is >= '1' and <= '8')
        {
            startKey = int.Parse(s[..1]);
            s = s[1..];
        }
        switch (s[0])
        {
            case '-': return SlideType.SI_;
            case 'v': return SlideType.SV_;
            case '<':
                Utils.Assert(startKey != null, "startKey没传进来");
                return startKey is >= 3 and <= 6 ? SlideType.SCR : SlideType.SCL;
            case '>':
                Utils.Assert(startKey != null, "startKey没传进来");
                return startKey is >= 3 and <= 6 ? SlideType.SCL : SlideType.SCR;
            case '^':
                Utils.Assert(startKey != null, "startKey没传进来");
                if (!int.TryParse(s[1..2], out var endKey)) throw new ArgumentException(string.Format(Locale.InvalidSlide, $"{startKey}{s}"));
                var distance = (endKey - startKey!.Value + 8) % 8; // 先假设按顺时针的方向走，看看距离
                if (distance is 0 or 4) throw new ArgumentException(string.Format(Locale.InvalidSlide, $"{startKey}{s}(^的endKey不能是整半圈)"));
                return distance < 4 ? SlideType.SCR : SlideType.SCL; // <4说明顺时针走更近；反之如果顺时针走的距离>4，则说明逆时针更近。
            case 'p':
                if (s.Length > 1 && s[1] == 'p') return SlideType.SXL; // pp
                return SlideType.SUL;
            case 'q': 
                if (s.Length > 1 && s[1] == 'q') return SlideType.SXR; // qq
                return SlideType.SUR;
            case 'V':
                Utils.Assert(startKey != null, "startKey没传进来");
                if (!int.TryParse(s[1..2], out var midKey)) throw new ArgumentException(string.Format(Locale.InvalidSlide, $"{startKey}{s}"));
                distance = (midKey - startKey!.Value + 8) % 8; // 先假设按顺时针的方向走，看看距离
                if (distance == 2) return SlideType.SLR;
                else if (distance == 6) return SlideType.SLL;
                else throw new ArgumentException(string.Format(Locale.InvalidSlide, $"{startKey}{s}"));
            case 's': return SlideType.SSL;
            case 'z': return SlideType.SSR;
            case 'w': return SlideType.SF_;
        }
        throw new ArgumentException(string.Format(Locale.InvalidSlide, $"{startKey}{s}"));
    }

    public static HashSet<string> SlideNames = Enum.GetNames<SlideType>().ToHashSet();
    
    public static bool IsSlide(string MA2Name)
    {
        return SlideNames.Contains(MA2Name[2..5]);
    }
}
