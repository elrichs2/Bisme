using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Bisme;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager        CommandManager   { get; private set; } = null!;
    [PluginService] public static IClientState           ClientState      { get; private set; } = null!;
    [PluginService] public static IObjectTable           ObjectTable      { get; private set; } = null!;
    [PluginService] public static IDataManager           DataManager      { get; private set; } = null!;
    [PluginService] public static IChatGui               Chat             { get; private set; } = null!;
    [PluginService] public static IPluginLog             Log              { get; private set; } = null!;

    private const string Cmd = "/bisme";

    // Gear slot index → xivgear key (matches order returned by EquippedItems container)
    // Order in InventoryType.EquippedItems is well known: 0..12
    private static readonly string?[] SlotIndexToName = {
        "MainHand",   // 0  - main weapon
        "OffHand",    // 1  - shield (PLD only)
        "Head",       // 2
        "Body",       // 3
        "Hand",       // 4  (Hands → "Hand" in xivgear)
        "Waist",      // 5  - obsolete since SHB but slot exists
        "Legs",       // 6
        "Feet",       // 7
        "Ears",       // 8
        "Neck",       // 9
        "Wrist",      // 10
        "RingLeft",   // 11
        "RingRight"   // 12
    };

    // Convert slot index to xivgear's expected key.
    private static string? XivgearKey(int idx)
    {
        // xivgear uses "Weapon" for the main hand slot
        if (idx == 0) return "Weapon";
        return SlotIndexToName.Length > idx ? SlotIndexToName[idx] : null;
    }

    public Plugin()
    {
        CommandManager.AddHandler(Cmd, new CommandInfo(OnCommand)
        {
            HelpMessage =
                "Export equipped gear + materia. Args:\n" +
                "  (no arg)   → print JSON to chat\n" +
                "  file       → save to %USERPROFILE%/Documents/Bisme.json\n" +
                "  clipboard  → copy JSON to clipboard"
        });
    }

    public void Dispose() => CommandManager.RemoveHandler(Cmd);

    private void OnCommand(string command, string arguments)
    {
        try
        {
            var json = BuildJson();
            var arg = arguments.Trim().ToLowerInvariant();
            switch (arg)
            {
                case "":
                case "chat":
                    Chat.Print($"[Bisme] {json}");
                    Chat.Print("[Bisme] (Tip: '/bisme file' or '/bisme clipboard')");
                    break;

                case "file":
                    var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    var path = Path.Combine(docs, "Bisme.json");
                    File.WriteAllText(path, json);
                    Chat.Print($"[Bisme] Saved → {path}");
                    break;

                case "clipboard":
                case "clip":
                    SetClipboard(json);
                    Chat.Print("[Bisme] Copied JSON to clipboard.");
                    break;

                default:
                    Chat.Print($"[Bisme] Unknown arg '{arg}'. Use: chat | file | clipboard");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bisme failed");
            Chat.PrintError($"[Bisme] Error: {ex.Message}");
        }
    }

    private unsafe string BuildJson()
    {
        var local = ObjectTable.LocalPlayer;
        if (local == null) throw new InvalidOperationException("LocalPlayer is null (login required).");

        var jobAbbr = local.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? "???";

        var inv = InventoryManager.Instance();
        if (inv == null) throw new InvalidOperationException("InventoryManager unavailable.");

        var equipped = inv->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null || !equipped->IsLoaded)
            throw new InvalidOperationException("Equipped items container not loaded.");

        var items = new Dictionary<string, object>();

        for (var i = 0; i < equipped->Size; i++)
        {
            var slotName = XivgearKey(i);
            if (slotName == null) continue;

            var slot = equipped->GetInventorySlot(i);
            if (slot == null) continue;

            var itemId = slot->ItemId;
            if (itemId == 0) continue; // empty slot

            // Strip HQ flag (1,000,000 added for HQ in inventory but xivgear uses base id)
            var baseId = itemId % 1000000;

            // Read materia (up to 5)
            var materiaList = new List<object>();
            for (var m = 0; m < 5; m++)
            {
                var matId = slot->Materia[m];
                if (matId == 0) break;
                // Xivgear expects the actual materia *item* ID, not the materia row.
                // Dalamud's slot->Materia[m] is the material row (1..n).
                // We need to map (materiaRow, grade) → item ID via the Materia sheet.
                var grade = slot->MateriaGrades[m];
                var actualItemId = ResolveMateriaItemId(matId, grade);
                materiaList.Add(new { id = actualItemId, locked = false });
            }

            items[slotName] = new
            {
                id = (int)baseId,
                materia = materiaList
            };
        }

        // Try to read currently selected food/medicine (best-effort: not always trivially exposed)
        // We skip food for now — user picks it in the optimizer UI.
        var sheet = new
        {
            name = $"{local.Name.TextValue} — {jobAbbr} Live Export",
            description = $"Exported from Bisme at {DateTime.Now:yyyy-MM-dd HH:mm}",
            job = jobAbbr,
            level = local.Level,
            partyBonus = 5,
            race = "Hyur",
            food = 0,
            items,
            sims = Array.Empty<object>(),
            customItems = Array.Empty<object>(),
            customFoods = Array.Empty<object>(),
            isSeparator = false,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return JsonSerializer.Serialize(sheet,
            new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Map (materiaRowId, grade) → actual Item id of that materia.
    /// </summary>
    private static uint ResolveMateriaItemId(ushort materiaRow, byte grade)
    {
        try
        {
            var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Materia>();
            var row = sheet.GetRow(materiaRow);
            if (grade < row.Item.Count)
                return row.Item[grade].RowId;
        }
        catch
        {
            // best-effort — if Lumina path differs across Dalamud versions, return 0
        }
        return 0;
    }

    private static void SetClipboard(string text)
    {
        // ImGui's clipboard works in Dalamud (uses the game's Win32 clipboard internally).
        ImGui.SetClipboardText(text);
    }
}
