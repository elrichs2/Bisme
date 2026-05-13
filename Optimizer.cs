using System;
using System.Collections.Generic;
using System.Linq;

namespace Bisme;

public static class Optimizer
{
    // All possible slots. OffHand is opt-in per-job (see JobsWithOffHand).
    public static readonly string[] Slots =
    {
        "Weapon","OffHand","Head","Body","Hand","Legs","Feet",
        "Ears","Neck","Wrist","RingLeft","RingRight"
    };

    // Jobs that equip an off-hand piece. PLD is the only endgame case;
    // extend this set if SE ever brings another shield-using job back.
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
        // Active content sync. 0 = no sync (open world, current Savage tier).
        // Non-zero values (e.g., 735 for FRU) make GetStatCap rescale per-piece
        // substat caps as if the gear were synced down. Items already below the
        // sync threshold keep their native cap.
        public int SyncIlvl { get; set; } = 0;

        public static State Empty()
        {
            var s = new State();
            // Pre-populate ALL slots; UI / optimizer ignore OffHand for jobs without it.
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

            // Shields have mSlots=0 -> Materia list is empty -> this loop is a no-op.
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

        // Gear-only stats
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

        // Step 1: pick best food
        var bestFood = PickBestFood(data, state);
        if (bestFood != null) state.FoodId = bestFood.Id;

        // Step 2: reset materia, build per-piece state + slot list
        var pieceStat = new Dictionary<string, Dictionary<string, int>>();
        var allSlots = new List<(string Slot, int Idx, BisItem Item, string Grade)>();

        foreach (var slot in SlotsForJob(state.Job))
        {
            var g = state.Gear.GetValueOrDefault(slot);
            if (g?.ItemId == null) continue;
            var it = data.GetItem(g.ItemId.Value);
            if (it == null) continue;
            pieceStat[slot] = Stats.ToDictionary(s => s, s => it.Stats.GetValueOrDefault(s, 0));

            // Shields: MSlots=0 and Adv=false -> sc=0, no melds added to allSlots.
            var sc = it.Adv ? 5 : it.MSlots;
            g.Materia = Enumerable.Repeat<string?>(null, sc).ToList();
            for (var i = 0; i < sc; i++)
                allSlots.Add((slot, i, it, data.GetSlotGrade(it, i)));
        }

        if (allSlots.Count == 0) return;

        // Total accumulator (gear + food + materia)
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

        // Step 3: greedy fill with 4-tier scoring. Per-piece cap respects the
        // active SyncIlvl so melds laid down here will not overcap when the
        // player enters synced content like FRU.
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

    public static void LoadBisGear(BisData data, State state)
    {
        foreach (var slot in SlotsForJob(state.Job))
        {
            var first = data.ItemsForJobSlot(state.Job, slot).FirstOrDefault();
            if (first != null)
            {
                state.Gear[slot].ItemId = first.Id;
                var sc = first.Adv ? 5 : first.MSlots;
                state.Gear[slot].Materia = Enumerable.Repeat<string?>(null, sc).ToList();
            }
        }
    }
}
