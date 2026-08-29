using MuConvert.chu;
using MuConvert.utils;
using static MuConvert.utils.ChuUtils;

namespace MuConvert.parser;

public abstract class BaseChuParser : IParser<ChuChart>
{
    public abstract (ChuChart, List<Alert>) Parse(string text);

    /**
     * 填充所有需要 Previous 的音符（见 <see cref="ChuNote.Previous"/> 注释）。
     * 只会填充当前Previous没有被设置过的音符：如果某个音符的Previous不为null（在Parse过程中已经被设置过了），则会尊重Parse的决定，不会再次设置。
     *
     * 推断规则：
     * - 前驱音符必须满足“首尾相接”：prev.EndTime == cur.Time 且 prev.EndCell == cur.Cell 且 prev.EndWidth == cur.Width
     * - 再按音符类型施加额外约束（slide / air / air-slide）
     *
     * 该方法应在所有音符解析完成后调用。
     *
     * <param name="chart">谱面对象</param>
     * <param name="alerts">过程中产生的警告会被放进这个数组里。</param>
     * <param name="rawTargetNote">可选。对C2S这种，谱面中原始记录了targetNote的类型的格式，可以将相关记录通过这个字典传过来，供本函数作为选择previous时的优先和参考。</param>
     */
    protected virtual void FillAllPrevious(ChuChart chart, List<Alert> alerts, Dictionary<ChuNote, string>? rawTargetNote = null)
    {
        if (chart.Notes.Count == 0) return;

        var endDict = new Dictionary<(Rationals.Rational EndTime, int EndCell, int EndWidth), List<ChuNote>>();
        foreach (var n in chart.Notes)
        {
            endDict.Add((n.EndTime, n.EndCell, n.EndWidth), n);
            if (n.Previous != null)
            { // 每个note最多只能成为一个其他note的previous，因此若某个note已经被预先标记为其他note的previous了，则它不能再被纳入考虑。
                var p = n.Previous;
                endDict.GetValueOrDefault((p.EndTime, p.EndCell, p.EndWidth))?.Remove(p);
            }
        }

        foreach (var cur in chart.Notes)
        {
            if (!NeedsPrevious(cur)) continue;
            if (cur.Previous != null) continue; // 若某些 parser 已提前填了 Previous，则保留

            var key = (cur.Time, cur.Cell, cur.Width);
            var candidates = endDict.GetValueOrDefault(key, []);
            var filtered = FilterPreviousCandidates(cur, candidates);

            if (rawTargetNote != null && rawTargetNote.TryGetValue(cur, out var target) && !string.IsNullOrEmpty(target))
            {
                var filteredByRaw = filtered.Where(x=>AsTargetType(x) == target).ToList();
                if (filteredByRaw.Count == 0)
                {
                    alerts.Add(new Alert(Alert.LEVEL.Warning, "未找到声明的前驱/依附音符", (chart, cur.Time)));
                }
                else filtered = filteredByRaw; // 缩小目标范围
            }

            if (filtered.Count > 0)
            {
                cur.Previous = filtered[0]; // 取第一个
                candidates.Remove(filtered[0]);
            }
        }
    }

    private static bool NeedsPrevious(ChuNote n)
    {
        return IsSlide(n.Type) || IsAir(n.Type) || IsAirHold(n.Type) || IsAirSlide(n.Type) || IsAirCrush(n.Type);
    }
    
    protected static List<ChuNote> FilterPreviousCandidates(ChuNote cur, List<ChuNote> candidates)
    { // 注意：候选列表已满足“首尾相接”，这里仅做类型约束
        List<ChuNote> result = [];
        candidates = candidates.Where(n => n != cur).ToList(); // 自己不能成为自己的candidate，防止自环
        
        if (IsSlide(cur.Type))
        { // Slide 的 previous：上一段 slide（找不到则说明是首段，则为 null）
            result.AddRange(candidates.Where(n => IsSlide(n.Type)));
        }
        else if (IsAirSlide(cur.Type))
        { // Air Slide：优先匹配“上一段airslide”，其次匹配“上一段其他
            result.AddRange(candidates.Where(n => IsAirSlide(n.Type) && n.EndHeight == cur.Height));
            result.AddRange(candidates.Where(n => !IsAirSlide(n.Type) && IsLegalPreviousForAir(n.Type)));
        }
        else if (IsAirHold(cur.Type))
        { // Air Hold：优先匹配“上一段airhold”，其次匹配“上一段其他
            result.AddRange(candidates.Where(n => IsAirHold(n.Type)));
            result.AddRange(candidates.Where(n => !IsAirHold(n.Type) && IsLegalPreviousForAir(n.Type)));
        }
        else if (IsAirCrush(cur.Type))
        { // Air Crush：只匹配“上一段aircrush”（且上一段必须具有正 duration）
            result.AddRange(candidates.Where(n => IsAirCrush(n.Type) && n.Tag == cur.Tag && n.EndHeight == cur.Height));
        }
        else if (IsAir(cur.Type))
        { // Air 系列：依附在一个“非广义Air”的音符上
            result.AddRange(candidates.Where(n => IsLegalPreviousForAir(n.Type)).ToList());
        }
        return result;
        
        bool IsLegalPreviousForAir(string t) => !(IsGeneralizedAir(t) || t == "MNE" || t == "CLICK");
    }
}