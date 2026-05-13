using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bisme;

public class BisItem
{
    [JsonPropertyName("id")]      public int Id { get; set; }
    [JsonPropertyName("name")]    public string Name { get; set; } = "";
    [JsonPropertyName("ilvl")]    public int Ilvl { get; set; }
    [JsonPropertyName("slot")]    public string Slot { get; set; } = "";
    [JsonPropertyName("stats")]   public Dictionary<string, int> Stats { get; set; } = new();
    [JsonPropertyName("mSlots")]  public int MSlots { get; set; }
    [JsonPropertyName("adv")]     public bool Adv { get; set; }
    [JsonPropertyName("hq")]      public bool Hq { get; set; }

    public override string ToString() => $"i{Ilvl} {Name}";
}

public class FoodBonus
{
    [JsonPropertyName("pct")] public int Pct { get; set; }
    [JsonPropertyName("max")] public int Max { get; set; }
}

public class BisFood
{
    [JsonPropertyName("id")]       public int Id { get; set; }
    [JsonPropertyName("name")]     public string Name { get; set; } = "";
    [JsonPropertyName("bonuses")]  public Dictionary<string, FoodBonus> Bonuses { get; set; } = new();
}

public class MateriaGrade
{
    [JsonPropertyName("id")]    public int Id { get; set; }
    [JsonPropertyName("value")] public int Value { get; set; }
}

public class BisGearpiece
{
    [JsonPropertyName("id")]    public int Id { get; set; }
    [JsonPropertyName("melds")] public List<string?> Melds { get; set; } = new();
}

public class BisGearset
{
    [JsonPropertyName("name")]   public string Name { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("food")]   public int? Food { get; set; }
    [JsonPropertyName("items")]  public Dictionary<string, BisGearpiece> Items { get; set; } = new();
}

public class BisData
{
    [JsonPropertyName("jobs")]            public List<string> Jobs { get; set; } = new();
    [JsonPropertyName("bis")]             public Dictionary<string, Dictionary<string, int>> Bis { get; set; } = new();
    [JsonPropertyName("items")]           public Dictionary<string, BisItem> Items { get; set; } = new();
    [JsonPropertyName("jobItems")]        public Dictionary<string, List<int>> JobItems { get; set; } = new();
    [JsonPropertyName("foods")]           public List<BisFood> Foods { get; set; } = new();
    [JsonPropertyName("ilvlCaps")]        public Dictionary<string, int> IlvlCaps { get; set; } = new();
    [JsonPropertyName("slotRatios")]      public Dictionary<string, double> SlotRatios { get; set; } = new();
    [JsonPropertyName("materiaGrades")]   public Dictionary<string, Dictionary<string, MateriaGrade>> MateriaGrades { get; set; } = new();
    [JsonPropertyName("syncIlvls")]       public Dictionary<string, int> SyncIlvls { get; set; } = new();
    // Curated BiS gearsets keyed by [job][contentLabel]. Each combo holds a
    // LIST of variants (e.g. different GCD speeds or weapon choices) so the
    // user can pick the build that matches their stat preference.
    [JsonPropertyName("bisGearsets")]     public Dictionary<string, Dictionary<string, List<BisGearset>>> BisGearsets { get; set; } = new();

    public Dictionary<int, string> MateriaIdToStat { get; private set; } = new();

    public static BisData Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames().First(n => n.EndsWith("data.json"));
        using var stream = asm.GetManifestResourceStream(resName)!;
        var data = JsonSerializer.Deserialize<BisData>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new System.Exception("Failed to parse data.json");

        foreach (var (grade, stats) in data.MateriaGrades)
            foreach (var (stat, mg) in stats)
                if (mg.Id != 0) data.MateriaIdToStat[mg.Id] = stat;

        return data;
    }

    public BisItem? GetItem(int id) =>
        Items.TryGetValue(id.ToString(), out var it) ? it : null;

    public IEnumerable<BisItem> ItemsForJobSlot(string job, string slot)
    {
        if (!JobItems.TryGetValue(job, out var ids)) return Enumerable.Empty<BisItem>();
        var slotKey = slot.StartsWith("Ring") ? "Ring" : slot;
        return ids
            .Select(id => GetItem(id))
            .Where(it => it != null && it.Slot == slotKey)
            .Cast<BisItem>()
            .OrderByDescending(it => it.Ilvl);
    }

    public int GetStatCap(BisItem item, int syncIlvl = 0)
    {
        var effectiveIlvl = (syncIlvl > 0 && syncIlvl < item.Ilvl) ? syncIlvl : item.Ilvl;
        if (!IlvlCaps.TryGetValue(effectiveIlvl.ToString(), out var cap)) return 0;
        if (!SlotRatios.TryGetValue(item.Slot, out var ratio)) return 0;
        return (int)System.Math.Round(cap * ratio);
    }

    public string GetSlotGrade(BisItem item, int slotIdx)
    {
        var xiiCount = item.MSlots + (item.Adv ? 1 : 0);
        return slotIdx < xiiCount ? "XII" : "XI";
    }

    public int GetGradeValue(string grade, string stat) =>
        MateriaGrades.TryGetValue(grade, out var g) && g.TryGetValue(stat, out var mg) ? mg.Value : 0;

    public int GetGradeMateriaId(string grade, string stat) =>
        MateriaGrades.TryGetValue(grade, out var g) && g.TryGetValue(stat, out var mg) ? mg.Id : 0;

    /// <summary>
    /// All curated variants for (job, contentLabel). Returns an empty list if
    /// nothing is curated -- the caller should then fall back to the algo.
    /// </summary>
    public List<BisGearset> GetCuratedVariants(string job, string contentLabel)
    {
        if (string.IsNullOrEmpty(contentLabel)) return new List<BisGearset>();
        if (!BisGearsets.TryGetValue(job, out var perJob)) return new List<BisGearset>();
        return perJob.TryGetValue(contentLabel, out var list) ? list : new List<BisGearset>();
    }

    /// <summary>
    /// Pick a single variant by index (clamped to valid range). Returns null
    /// if the combo has no curated data.
    /// </summary>
    public BisGearset? GetCuratedGearset(string job, string contentLabel, int variantIdx)
    {
        var list = GetCuratedVariants(job, contentLabel);
        if (list.Count == 0) return null;
        if (variantIdx < 0) variantIdx = 0;
        if (variantIdx >= list.Count) variantIdx = list.Count - 1;
        return list[variantIdx];
    }
}
