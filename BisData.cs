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
    // Maps a content-mode label (UI dropdown) to its ilvl sync target. A value of 0
    // means no sync. Used by GetStatCap to recompute per-piece caps when running
    // sync'd content like Ultimates.
    [JsonPropertyName("syncIlvls")]       public Dictionary<string, int> SyncIlvls { get; set; } = new();

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

    /// <summary>
    /// Computes the per-piece substat cap for an item. When <paramref name="syncIlvl"/>
    /// is greater than 0 and lower than the item's native ilvl, the cap is computed
    /// at the sync ilvl (Ultimate-style content). Items already below the sync
    /// threshold keep their native cap.
    /// </summary>
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
}
