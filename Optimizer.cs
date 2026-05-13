using System;
using System.Collections.Generic;
using System.Linq;

namespace Bisme;

public static class Optimizer
{
    public static readonly string[] Slots =
    {
        "Weapon","OffHand","Head","Body","Hand","Legs","Feet",
        "Ears","Neck","Wrist","RingLeft","RingRight"
    };

    public static readonly HashSet<string> JobsWithOffHand = new() { "PLD" };

    public static IEnumerable<string> SlotsForJob(string job) =>
        Slots.Where(s => s != "OffHand" || JobsWithOffHand.Contains(job));

    public static readonly string[] Stats =
    {
        "CRT","DET","DH","SKS","SPS","TEN","PIE"
    };

    public static readonly Dictionary<string, string> StatNames = new()
    {
        ["CRT"] = "Crit", ["DET"] = "Det", ["DH"] = "DH",
        ["SKS"] = "SkS", ["SPS"] = "SpS", ["TEN"] = "Ten", ["PIE"] = "Piety"
    };

    private static readonly Dictionary<string, string> BisKeyMap = new()
    {
        ["Crit"] = "CRT", ["Det"] = "DET", ["DH"] = "DH",
        ["SkS"] = "SKS", ["SpS"] = "SPS", ["Tenacity"] = "TEN", ["Piety"] = "PIE"
    };

    private static readonly Dictionary<int, string> ParamToStat = new()
    {
        [6]  = "PIE", [19] = "TEN", [22] = "DH",
        [27] = "CRT", [44] = "DET", [45] = "SKS", [46] = "SPS"
    };

    public class GearSlot
    {
        public int? ItemId { get; set; }
        public List<string?> Materia { get; set; } = new();
    }

    public class State
    {
        public string Job { get; set; } = "WAR";
        public Dictionary<string, GearSlot> Gear { get; set; } = new();
        public int? FoodId { get; set; }
        public int SyncIlvl { get; set; } = 0;
        public string ContentMode { get; set; } = "";
        // Index into the curated variants list for the active (job, content).
        // Reset to 0 whenever the combo changes.
        public int BisVariantIdx { get; set; } = 0;

        public static State Empty()
        {
            var s = new State();
            foreach (var slot in Slots) s.Gear[slot] = new GearSlot();
            return s;
        }
    }

    public static Dictionary<string, int> BisTarget(BisData data, string job)
    {
        var result = Stats.ToDictionary(s => s, _ => 0);
        if (!data.Bis.TryGetValue(job, out var bis)) return result;
        foreach (var (k, v) in bis)
            if (BisKeyMap.TryGetValue(k, out var ik) && result.ContainsKey(ik))
                result[ik] = v;
        return result;
    }

    public static List<string> RelevantStats(BisData data, string job) =>
        BisTarget(data, job).Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();

    public static Dictionary<string, int> FoodBonus(BisFood food, Dictionary<string, int> baseStats)
    {
        var result = Stats.ToDictionary(s => s, _ => 0);
        foreach (var (bpStr, b) in food.Bonuses)
        {
            if (!int.TryParse(bpStr, out var bp)) continue;
            if (!ParamToStat.TryGetValue(bp, out var stat)) continue;
            var baseVal = baseStats.GetValueOrDefault(stat, 0) + 420;
            result[stat] = Math.Min((int)(baseVal * b.Pct / 100.0), b.Max);
        }
        return result;
    }

    public static Dictionary<string, int> ComputeStats(BisData data, State state, bool includeFood = true)
    {
        var totals = Stats.ToDictionary(s => s, _ => 0);

        foreach (var slot in SlotsForJob(state.Job))
        {
            var g = state.Gear.GetValueOrDefault(slot);
            if (g?.ItemId == null) continue;
            var it = data.GetItem(g.ItemId.Value);
            if (it == null) continue;

            var cap = data.GetStatCap(it, state.SyncIlvl);
            var piece = Stats.ToDictionary(s => s, s => it.Stats.GetValueOrDefault(s, 0));

            for (var i = 0; i < g.Materia.Count; i++)
            {
                var stat = g.Materia[i];
                if (stat == null || !piece.ContainsKey(stat)) continue;
                var grade = data.GetSlotGrade(it, i);
                var gv = data.GetGradeValue(grade, stat);
                var headroom = Math.Max(0, cap - piece[stat]);
                piece[stat] += Math.Min(gv, headroom);
            }

            foreach (var s in Stats) totals[s] += piece[s];
        }

        if (includeFood && state.FoodId.HasValue)
        {
            var food = data.Foods.FirstOrDefault(f => f.Id == state.FoodId.Value);
            if (food != null)
            {
                var fb = FoodBonus(food, totals);
                foreach (var s in Stats) totals[s] += fb[s];
            }
        }

        return totals;
    }

    public static BisFood? PickBestFood(BisData data, State state)
    {
        var target = BisTarget(data, state.Job);
        var relevant = RelevantStats(data, state.Job);

        var gearOnly = Stats.ToDictionary(s => s, _ => 0);
        foreach (var slot in SlotsForJob(state.Job))
        {
            var g = state.Gear.GetValueOrDefault(slot);
            if (g?.ItemId == null) continue;
            var it = data.GetItem(g.ItemId.Value);
            if (it == null) continue;
            foreach (var (s, v) in it.Stats)
                if (gearOnly.ContainsKey(s)) gearOnly[s] += v;
        }

        var deficit = Stats.ToDictionary(s => s, s => Math.Max(0, target[s] - gearOnly[s]));

        BisFood? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var food in data.Foods)
        {
            var fb = FoodBonus(food, gearOnly);
            var useful = relevant.Sum(s => Math.Min(fb[s], deficit[s]));
            var waste = Stats.Where(s => !relevant.Contains(s)).Sum(s => fb[s]);
            var score = useful - waste * 0.5;
            if (score > bestScore) { bestScore = score; best = food; }
        }
        return best;
    }

    public static void OptimizeMateria(BisData data, State state)
    {
        var target = BisTarget(data, state.Job);

        var bestFood = PickBestFood(data, state);
        if (bestFood != null) state.FoodId = bestFood.Id;

        var pieceStat = new Dictionary<string, Dictionary<string, int>>();
        var allSlots = new List<(string Slot, int Idx, BisItem Item, string Grade)>();

        foreach (var slot in SlotsForJob(state.Job))
        {
            var g = state.Gear.GetValueOrDefault(slot);
            if (g?.ItemId == null) continue;
            var it = data.GetItem(g.ItemId.Value);
            if (it == null) continue;
            pieceStat[slot] = Stats.ToDictionary(s => s, s => it.Stats.GetValueOrDefault(s, 0));

            var sc = it.Adv ? 5 : it.MSlots;
            g.Materia = Enumerable.Repeat<string?>(null, sc).ToList();
            for (var i = 0; i < sc; i++)
                allSlots.Add((slot, i, it, data.GetSlotGrade(it, i)));
        }

        if (allSlots.Count == 0) return;

        var total = Stats.ToDictionary(s => s, _ => 0);
        foreach (var slot in pieceStat.Keys)
            foreach (var s in Stats) total[s] += pieceStat[slot][s];
        if (state.FoodId.HasValue)
        {
            var food = data.Foods.FirstOrDefault(f => f.Id == state.FoodId.Value);
            if (food != null)
            {
                var fb = FoodBonus(food, total);
                foreach (var s in Stats) total[s] += fb[s];
            }
        }

        var relevant = RelevantStats(data, state.Job);

        foreach (var ms in allSlots)
        {
            var cap = data.GetStatCap(ms.Item, state.SyncIlvl);
            string? best = null;
            var bestScore = double.NegativeInfinity;
            var bestGain = 0;

            foreach (var stat in relevant)
            {
                var headroom = Math.Max(0, cap - pieceStat[ms.Slot][stat]);
                var gv = data.GetGradeValue(ms.Grade, stat);
                var gain = Math.Min(gv, headroom);
                var gap = target[stat] - total[stat];
                var fullGain = gain == gv;

                double score;
                if (fullGain && gap > 0)  score = 30_000_000 + Math.Min(gap, gain);
                else if (fullGain)        score = 20_000_000 + gain;
                else if (gain > 0)        score = 10_000_000 + gain + (gap > 0 ? 1000 : 0);
                else                      score = 0 + gap;

                if (score > bestScore) { bestScore = score; best = stat; bestGain = gain; }
            }

            if (best != null)
            {
                state.Gear[ms.Slot].Materia[ms.Idx] = best;
                pieceStat[ms.Slot][best] += bestGain;
                total[best] += bestGain;
            }
        }
    }

    /// <summary>
    /// Load the BiS gearset for the current (Job, ContentMode, BisVariantIdx).
    /// Curated theorycraft data takes precedence; the algo fallback runs only
    /// if no curated set exists for this combo.
    /// </summary>
    public static void LoadBisGear(BisData data, State state)
    {
        var curated = data.GetCuratedGearset(state.Job, state.ContentMode, state.BisVariantIdx);
        if (curated != null)
        {
            foreach (var slot in Slots)
                state.Gear[slot] = new GearSlot();

            foreach (var (slot, piece) in curated.Items)
            {
                if (!state.Gear.ContainsKey(slot)) continue;
                var it = data.GetItem(piece.Id);
                if (it == null) continue;

                var gs = new GearSlot { ItemId = piece.Id };
                var sc = it.Adv ? 5 : it.MSlots;
                gs.Materia = Enumerable.Repeat<string?>(null, sc).ToList();
                for (var i = 0; i < piece.Melds.Count && i < sc; i++)
                    gs.Materia[i] = piece.Melds[i];

                state.Gear[slot] = gs;
            }

            if (curated.Food.HasValue) state.FoodId = curated.Food;
            return;
        }

        LoadBisGearAlgo(data, state);
    }

    private static void LoadBisGearAlgo(BisData data, State state)
    {
        var relevant = RelevantStats(data, state.Job);
        double syncCapBase = 0;
        if (state.SyncIlvl > 0 && data.IlvlCaps.TryGetValue(state.SyncIlvl.ToString(), out var sc))
            syncCapBase = sc;

        foreach (var slot in SlotsForJob(state.Job))
        {
            var candidates = data.ItemsForJobSlot(state.Job, slot).ToList();
            BisItem? pick = null;
            double bestScore = double.NegativeInfinity;

            foreach (var it in candidates)
            {
                double scale = 1.0;
                if (syncCapBase > 0 && it.Ilvl > state.SyncIlvl
                    && data.IlvlCaps.TryGetValue(it.Ilvl.ToString(), out var nc) && nc > 0)
                {
                    scale = syncCapBase / nc;
                }

                double useful = relevant.Sum(s => it.Stats.GetValueOrDefault(s, 0)) * scale;
                int meldCount = it.Adv ? 5 : it.MSlots;
                double meldBudget = meldCount * 60.0;
                double score = useful + meldBudget;

                if (score > bestScore) { bestScore = score; pick = it; }
            }

            if (pick != null)
            {
                state.Gear[slot].ItemId = pick.Id;
                var msc = pick.Adv ? 5 : pick.MSlots;
                state.Gear[slot].Materia = Enumerable.Repeat<string?>(null, msc).ToList();
            }
        }
    }
}
