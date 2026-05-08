using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Bisme;

/// <summary>
/// Serializes a Bisme state into BisBuddy's JSON import format.
/// Schema reverse-engineered from BisBuddy's GearsetConverter / GearpieceConverter / MateriaConverter.
/// </summary>
public static class BisBuddyExport
{
    private static readonly Dictionary<string, uint> JobToClassJobId = new()
    {
        // Tanks
        ["PLD"] = 19, ["WAR"] = 21, ["DRK"] = 32, ["GNB"] = 37,
        // Healers
        ["WHM"] = 24, ["SCH"] = 28, ["AST"] = 33, ["SGE"] = 40,
        // Melee
        ["MNK"] = 20, ["DRG"] = 22, ["NIN"] = 30, ["SAM"] = 34, ["RPR"] = 39, ["VPR"] = 41,
        // Phys ranged
        ["BRD"] = 23, ["MCH"] = 31, ["DNC"] = 38,
        // Casters
        ["BLM"] = 25, ["SMN"] = 27, ["RDM"] = 35, ["PCT"] = 42,
    };

    public static string Build(BisData data, Optimizer.State state)
    {
        var classJobId = JobToClassJobId.GetValueOrDefault(state.Job, 0u);

        var gearpieces = new List<object>();
        foreach (var slotKey in Optimizer.Slots)
        {
            var g = state.Gear.GetValueOrDefault(slotKey);
            if (g?.ItemId == null) continue;
            var item = data.GetItem(g.ItemId.Value);
            if (item == null) continue;

            var materiaList = new List<object>();
            for (var i = 0; i < g.Materia.Count; i++)
            {
                var stat = g.Materia[i];
                if (stat == null) continue;
                var grade = data.GetSlotGrade(item, i);
                var matId = data.GetGradeMateriaId(grade, stat);
                if (matId == 0) continue;
                materiaList.Add(new
                {
                    ItemId = (uint)matId,
                    IsCollected = false,
                    CollectLock = false
                });
            }

            gearpieces.Add(new
            {
                ItemId = (uint)g.ItemId.Value,
                IsCollected = false,
                CollectLock = false,
                ItemMateria = materiaList,
                PrerequisiteTree = (object?)null
            });
        }

        var gearset = new
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"Bisme — {state.Job} {DateTime.Now:yyyy-MM-dd HH:mm}",
            SourceType = 2, // 2 = Json
            ClassJobId = classJobId,
            IsActive = true,
            SourceUrl = (string?)null,
            SourceString = (string?)null,
            Priority = 0,
            ImportDate = DateTime.UtcNow,
            HighlightColor = (object?)null,
            Gearpieces = gearpieces,
        };

        return JsonSerializer.Serialize(gearset, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}
