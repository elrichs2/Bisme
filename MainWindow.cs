using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Bisme;

public class MainWindow : Window, IDisposable
{
    private readonly BisData _data;
    private Optimizer.State _state;

    // Cached UI strings
    private readonly string[] _jobLabels;
    private readonly string[] _foodLabels;
    private readonly string[] _statLabels;

    public MainWindow(BisData data) : base("Bisme — FFXIV Meld Optimizer", ImGuiWindowFlags.None)
    {
        _data = data;
        _state = Optimizer.State.Empty();
        _state.Job = "WAR";

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 600),
            MaximumSize = new Vector2(2400, 1600),
        };

        _jobLabels = _data.Jobs.ToArray();
        _foodLabels = new[] { "— None —" }.Concat(_data.Foods.Select(f =>
        {
            var stats = string.Join(", ", f.Bonuses.Select(b =>
            {
                var statName = b.Key switch
                {
                    "6" => "Pty", "19" => "Ten", "22" => "DH",
                    "27" => "Crit", "44" => "Det", "45" => "SkS", "46" => "SpS",
                    _ => b.Key
                };
                return $"{statName}+{b.Value.Max}";
            }));
            return $"{f.Name} ({stats})";
        })).ToArray();
        _statLabels = Optimizer.Stats;
    }

    public void Dispose() { }

    /// <summary>Public hook so the Plugin can populate state from equipped gear.</summary>
    public void LoadEquippedGear()
    {
        try { Plugin.LoadEquippedIntoState(_state); }
        catch (Exception e) { Plugin.Chat.PrintError($"[Bisme] {e.Message}"); }
    }

    /// <summary>Public hook so the Plugin can trigger auto-optimize.</summary>
    public void RunOptimize() => Optimizer.OptimizeMateria(_data, _state);

    public override void Draw()
    {
        DrawTopBar();
        ImGui.Separator();
        DrawFoodBar();
        ImGui.Separator();
        DrawTotalsBar();
        ImGui.Separator();

        // Two-column layout
        var avail = ImGui.GetContentRegionAvail();
        var leftWidth = avail.X * 0.62f;

        ImGui.BeginChild("##gear", new Vector2(leftWidth, 0), true);
        ImGui.TextColored(new Vector4(1f, 0.6f, 0f, 1f), "Gear & Melds");
        ImGui.Separator();
        DrawGearTable();
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##stats", new Vector2(0, 0), true);
        ImGui.TextColored(new Vector4(1f, 0.6f, 0f, 1f), "Stats vs BiS Target");
        ImGui.Separator();
        DrawStatsPanel();
        ImGui.EndChild();
    }

    private void DrawTopBar()
    {
        ImGui.Text("Job:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        var jobIdx = Array.IndexOf(_jobLabels, _state.Job);
        if (jobIdx < 0) jobIdx = 0;
        if (ImGui.Combo("##job", ref jobIdx, _jobLabels, _jobLabels.Length))
        {
            _state.Job = _jobLabels[jobIdx];
            ResetGear();
        }

        ImGui.SameLine();
        if (ImGui.Button("Load BiS Gear")) Optimizer.LoadBisGear(_data, _state);
        ImGui.SameLine();
        if (ImGui.Button("Load Equipped"))
        {
            try { Plugin.LoadEquippedIntoState(_state); }
            catch (Exception e) { Plugin.Chat.PrintError($"[Bisme] {e.Message}"); }
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset")) ResetGear();
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 0.6f, 0f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0f, 0f, 1f));
        if (ImGui.Button("Auto-Optimize Materia"))
            Optimizer.OptimizeMateria(_data, _state);
        ImGui.PopStyleColor(2);
    }

    private void DrawFoodBar()
    {
        ImGui.Text("Food:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(380);
        var foodIdx = 0;
        if (_state.FoodId.HasValue)
        {
            for (int i = 0; i < _data.Foods.Count; i++)
                if (_data.Foods[i].Id == _state.FoodId.Value) { foodIdx = i + 1; break; }
        }
        if (ImGui.Combo("##food", ref foodIdx, _foodLabels, _foodLabels.Length))
        {
            _state.FoodId = foodIdx == 0 ? (int?)null : _data.Foods[foodIdx - 1].Id;
        }

        if (_state.FoodId.HasValue)
        {
            var food = _data.Foods.FirstOrDefault(f => f.Id == _state.FoodId.Value);
            if (food != null)
            {
                var stats = Optimizer.ComputeStats(_data, _state, includeFood: false);
                var fb = Optimizer.FoodBonus(food, stats);
                var bonusStr = string.Join(", ",
                    Optimizer.Stats.Where(s => fb[s] > 0)
                                   .Select(s => $"{Optimizer.StatNames[s]} +{fb[s]}"));
                if (!string.IsNullOrEmpty(bonusStr))
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"→ {bonusStr}");
                }
            }
        }
    }

    private void DrawTotalsBar()
    {
        var target = Optimizer.BisTarget(_data, _state.Job);
        var relevant = Optimizer.RelevantStats(_data, _state.Job);
        var targetStr = string.Join(" • ",
            relevant.Select(s => $"{Optimizer.StatNames[s]}: {target[s]}"));

        int filled = 0, totalSlots = 0;
        foreach (var slot in Optimizer.Slots)
        {
            var g = _state.Gear.GetValueOrDefault(slot);
            if (g?.ItemId == null) continue;
            var it = _data.GetItem(g.ItemId.Value);
            if (it == null) continue;
            totalSlots += it.Adv ? 5 : it.MSlots;
            filled += g.Materia.Count(m => m != null);
        }

        ImGui.TextColored(new Vector4(1f, 0.6f, 0f, 1f), $"BiS {_state.Job}:");
        ImGui.SameLine();
        ImGui.Text(targetStr);
        ImGui.SameLine();
        ImGui.TextDisabled($"|  Materia: {filled}/{totalSlots} placed");
    }

    private void DrawGearTable()
    {
        if (!ImGui.BeginTable("gear_table", 7,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            return;

        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 3.5f);
        for (int i = 0; i < 5; i++)
            ImGui.TableSetupColumn($"M{i + 1}", ImGuiTableColumnFlags.WidthStretch, 1f);

        foreach (var slot in Optimizer.Slots)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(slot.Replace("Left", "L").Replace("Right", "R"));

            ImGui.TableNextColumn();
            DrawItemCombo(slot);

            var g = _state.Gear[slot];
            BisItem? it = g.ItemId.HasValue ? _data.GetItem(g.ItemId.Value) : null;

            // 5 materia columns; show only as many as the item has slots
            for (int i = 0; i < 5; i++)
            {
                ImGui.TableNextColumn();
                if (it != null && i < g.Materia.Count)
                    DrawMateriaCombo(slot, i, it);
            }
        }
        ImGui.EndTable();
    }

    private void DrawItemCombo(string slot)
    {
        var g = _state.Gear[slot];
        var items = _data.ItemsForJobSlot(_state.Job, slot).ToList();
        var currentLabel = "— Choose item —";
        if (g.ItemId.HasValue)
        {
            var it = _data.GetItem(g.ItemId.Value);
            if (it != null) currentLabel = $"i{it.Ilvl} • {it.Name}";
        }

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo($"##item_{slot}", currentLabel))
        {
            if (ImGui.Selectable("— Choose item —", !g.ItemId.HasValue))
            {
                g.ItemId = null;
                g.Materia.Clear();
            }
            foreach (var it in items)
            {
                bool selected = g.ItemId == it.Id;
                if (ImGui.Selectable($"i{it.Ilvl} • {it.Name}", selected))
                {
                    g.ItemId = it.Id;
                    var sc = it.Adv ? 5 : it.MSlots;
                    g.Materia = Enumerable.Repeat<string?>(null, sc).ToList();
                }
            }
            ImGui.EndCombo();
        }
    }

    private void DrawMateriaCombo(string slot, int idx, BisItem item)
    {
        var g = _state.Gear[slot];
        var grade = _data.GetSlotGrade(item, idx);
        var current = g.Materia[idx];
        var label = current == null ? "— empty —" : $"{Optimizer.StatNames[current]} +{_data.GetGradeValue(grade, current)}";

        // Color the advanced (overmeld) slots purple
        var isAdv = idx >= item.MSlots;
        if (isAdv) ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.32f, 0.18f, 0.38f, 1f));

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo($"##m_{slot}_{idx}", label))
        {
            if (ImGui.Selectable("— empty —", current == null))
                g.Materia[idx] = null;
            foreach (var stat in Optimizer.Stats)
            {
                var v = _data.GetGradeValue(grade, stat);
                bool sel = current == stat;
                if (ImGui.Selectable($"{Optimizer.StatNames[stat]} +{v}", sel))
                    g.Materia[idx] = stat;
            }
            ImGui.EndCombo();
        }

        if (isAdv) ImGui.PopStyleColor();
    }

    private void DrawStatsPanel()
    {
        var current = Optimizer.ComputeStats(_data, _state);
        var target = Optimizer.BisTarget(_data, _state.Job);

        if (!ImGui.BeginTable("stats_table", 3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            return;

        ImGui.TableSetupColumn("Stat", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Current / Target", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableSetupColumn("Δ", ImGuiTableColumnFlags.WidthFixed, 70);

        foreach (var s in Optimizer.Stats)
        {
            var c = current[s];
            var t = target[s];
            if (t == 0 && c == 0) continue;
            var d = c - t;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Optimizer.StatNames[s]);
            ImGui.TableNextColumn();
            ImGui.Text($"{c}");
            ImGui.SameLine();
            ImGui.TextDisabled($"/ {t}");
            ImGui.TableNextColumn();
            Vector4 col;
            if (t == 0) col = new Vector4(0.5f, 0.5f, 0.5f, 1f);
            else if (Math.Abs(d) <= 27) col = new Vector4(0.3f, 0.85f, 0.3f, 1f);
            else if (d > 0) col = new Vector4(1f, 0.6f, 0.2f, 1f);
            else col = new Vector4(0.95f, 0.3f, 0.3f, 1f);
            ImGui.TextColored(col, d >= 0 ? $"+{d}" : $"{d}");
        }
        ImGui.EndTable();

        ImGui.Spacing();
        ImGui.TextWrapped(
            "Values = gear + materia + food (without +420 base). Optimizer respects per-piece caps " +
            "and uses grade XII on base + 1st advanced slot, grade XI on later overmelds.");
    }

    private void ResetGear()
    {
        foreach (var slot in Optimizer.Slots)
            _state.Gear[slot] = new Optimizer.GearSlot();
    }
}
