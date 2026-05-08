using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
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
    [PluginService] public static IFramework             Framework        { get; private set; } = null!;

    private const string CmdMain = "/bisme";

    private readonly WindowSystem _windowSystem = new("Bisme");
    private readonly MainWindow _mainWindow;
    private readonly BisData _data;

    public static Plugin Instance { get; private set; } = null!;

    public Plugin()
    {
        Instance = this;
        try
        {
            _data = BisData.Load();
            Log.Information($"[Bisme] Loaded {_data.Items.Count} items, {_data.Foods.Count} foods, {_data.Jobs.Count} jobs.");
        }
        catch (Exception e)
        {
            Log.Error(e, "[Bisme] Failed to load data.json");
            throw;
        }

        _mainWindow = new MainWindow(_data);
        _windowSystem.AddWindow(_mainWindow);

        PluginInterface.UiBuilder.Draw       += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += () => _mainWindow.IsOpen = true;

        CommandManager.AddHandler(CmdMain, new CommandInfo(OnCommand)
        {
            HelpMessage =
                "Open Bisme meld optimizer. Args:\n" +
                "  (no arg)   -> toggle the optimizer window\n" +
                "  load       -> open window + load currently equipped gear\n" +
                "  optimize   -> open window + load equipped + auto-optimize"
        });

        // Auto-sync the optimizer to the player's current job, both at plugin load
        // and whenever they switch class in-game.
        ClientState.ClassJobChanged += OnClassJobChanged;
        Framework.RunOnTick(() =>
        {
            try
            {
                if (ClientState.IsLoggedIn && ObjectTable.LocalPlayer != null)
                    OnClassJobChanged(ObjectTable.LocalPlayer.ClassJob.RowId);
            }
            catch (Exception e) { Log.Warning(e, "[Bisme] Initial class sync failed"); }
        }, delayTicks: 30);
    }

    public void Dispose()
    {
        ClientState.ClassJobChanged -= OnClassJobChanged;
        CommandManager.RemoveHandler(CmdMain);
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _windowSystem.RemoveAllWindows();
        _mainWindow.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        try
        {
            var arg = arguments.Trim().ToLowerInvariant();
            switch (arg)
            {
                case "":
                    _mainWindow.IsOpen = !_mainWindow.IsOpen;
                    break;
                case "load":
                case "equipped":
                    _mainWindow.IsOpen = true;
                    _mainWindow.LoadEquippedGear();
                    break;
                case "optimize":
                case "opt":
                    _mainWindow.IsOpen = true;
                    _mainWindow.LoadEquippedGear();
                    _mainWindow.RunOptimize();
                    break;
                default:
                    Chat.Print($"[Bisme] Unknown arg '{arg}'. Use: (none) | load | optimize");
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "[Bisme] Command failed");
            Chat.PrintError($"[Bisme] {e.Message}");
        }
    }

    private void OnClassJobChanged(uint classJobId)
    {
        try
        {
            var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();
            var row = sheet.GetRow(classJobId);
            var abbr = row.Abbreviation.ExtractText();
            if (string.IsNullOrWhiteSpace(abbr)) return;

            // Defer slightly so the inventory has time to settle after the switch.
            Framework.RunOnTick(() =>
            {
                try { _mainWindow.SyncToJob(abbr); }
                catch (Exception e) { Log.Warning(e, "[Bisme] SyncToJob failed"); }
            }, delayTicks: 10);
        }
        catch (Exception e)
        {
            Log.Error(e, "[Bisme] Class change handler failed");
        }
    }

    public static unsafe void LoadEquippedIntoState(Optimizer.State state)
    {
        var local = ObjectTable.LocalPlayer;
        if (local == null) throw new InvalidOperationException("LocalPlayer is null (login required).");

        var jobAbbr = local.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? "???";
        if (Instance._data.Jobs.Contains(jobAbbr)) state.Job = jobAbbr;

        var inv = InventoryManager.Instance();
        if (inv == null) throw new InvalidOperationException("InventoryManager unavailable.");

        var equipped = inv->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null || !equipped->IsLoaded)
            throw new InvalidOperationException("Equipped items container not loaded.");

        foreach (var slot in Optimizer.Slots)
            state.Gear[slot] = new Optimizer.GearSlot();

        string?[] mapping =
        {
            "Weapon",     // 0
            null,         // 1 offhand
            "Head",       // 2
            "Body",       // 3
            "Hand",       // 4
            null,         // 5 waist
            "Legs",       // 6
            "Feet",       // 7
            "Ears",       // 8
            "Neck",       // 9
            "Wrist",      // 10
            "RingLeft",   // 11
            "RingRight"   // 12
        };

        for (var i = 0; i < equipped->Size; i++)
        {
            if (i >= mapping.Length) continue;
            var slotKey = mapping[i];
            if (slotKey == null) continue;

            var slot = equipped->GetInventorySlot(i);
            if (slot == null) continue;
            var itemId = (int)(slot->ItemId % 1000000);
            if (itemId == 0) continue;

            var item = Instance._data.GetItem(itemId);
            if (item == null) continue;

            var gs = new Optimizer.GearSlot { ItemId = itemId };
            var sc = item.Adv ? 5 : item.MSlots;
            gs.Materia = Enumerable.Repeat<string?>(null, sc).ToList();

            for (var m = 0; m < 5 && m < sc; m++)
            {
                var matRow = slot->Materia[m];
                if (matRow == 0) break;
                var grade = slot->MateriaGrades[m];
                var stat = ResolveMateriaStat(matRow, grade);
                if (stat != null) gs.Materia[m] = stat;
            }

            state.Gear[slotKey] = gs;
        }
    }

    private static unsafe string? ResolveMateriaStat(ushort materiaRow, byte grade)
    {
        try
        {
            var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Materia>();
            var row = sheet.GetRow(materiaRow);
            if (grade >= row.Item.Count) return null;
            var itemId = (int)row.Item[grade].RowId;
            return Instance._data.MateriaIdToStat.GetValueOrDefault(itemId);
        }
        catch { return null; }
    }
}
