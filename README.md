# Bisme

In-game meld optimizer for Final Fantasy XIV, packaged as a Dalamud plugin.
Pick a job, load your equipped gear, hit Auto-Optimize. Bisme picks the best
food and places materia to hit the BiS target while respecting per-piece
substat caps. One click sends the result to BisBuddy for farming.

The plugin is strictly read-only. It reads inventory through Dalamud's
`InventoryManager`, never sends packets, never modifies game state.

![patch 7.4](https://img.shields.io/badge/patch-7.4-orange) ![DalamudApiLevel 15](https://img.shields.io/badge/api-15-blue)

## What it does

- **Job-aware optimizer.** 21 jobs, BiS targets embedded for the i790
  Heavyweight Savage tier. Materia placement uses a four-tier greedy score
  (no overcap > partial overcap > overshoot > capped) and the per-piece cap
  formula `ItemLevel.SubStatCap x slot_ratio`. Grade XII goes on base and
  first overmeld slots, XI on the rest.
- **Off-hand support.** PLD shields are a first-class slot: stat
  contribution is added to totals, the row is hidden for jobs without an
  off-hand, and shields skip the materia columns since they have no meld
  slots. The mechanism is generic via a `JobsWithOffHand` set.
- **Live equipped-gear loader.** Reads items, materia rows, and grades
  straight from your inventory, including HQ flags.
- **Auto-sync on class change.** Switching class in-game retargets the
  optimizer and reloads gear with a small delay so the inventory has time
  to settle.
- **One-click BisBuddy export.** Serializes the current state into
  BisBuddy's `JsonSource` format (HQ-aware: `ItemId + 1_000_000` for
  HQ-able crafted gear, raw ID for tome and savage items) and copies to the
  clipboard.
- **Full ImGui interface.** Job picker, food picker, every gear slot with
  per-meld dropdowns, and a stats-vs-target panel with color-coded deltas.

## Install

1. In-game, run `/xlsettings`.
2. Open the **Experimental** tab, find **Custom Plugin Repositories**.
3. Add `https://raw.githubusercontent.com/elrichs2/Bisme/main/pluginmaster.json`,
   tick **Enabled**, then **Save and Close**.
4. Run `/xlplugins`, switch to **All Plugins**, search for **Bisme**, and
   click **Install**.

## Use

Three slash commands cover the common cases:

```
/bisme            toggle the window
/bisme load       open and load currently equipped gear
/bisme optimize   open, load equipped, run auto-optimize
```

The typical loop is:

1. Run `/bisme load`. The window opens with your job and gear pre-filled.
2. Click **Auto-Optimize Materia**. Bisme picks a food and lays out the
   melds.
3. Glance at the **Stats vs BiS Target** panel. Deltas within +/- 27
   (one substat tier) are green. Anything else needs attention.
4. Click **Send to BisBuddy**. The JSON is now on your clipboard.
5. In BisBuddy, **Add Gearset > JSON**, paste, **Import**. BisBuddy will
   highlight the items you still need across loot windows, vendors, the
   marketboard, and the meld interface.

When you change class in-game, Bisme retargets and reloads automatically.

## Limitations

- The active food buff is not auto-detected. Dalamud does not expose this
  reliably, so food is selected manually in the UI.
- The pre-Shadowbringers waist slot is intentionally skipped in the
  inventory mapping.

## Ban risk

This is the safest category of Dalamud plugin. It only reads memory, on
par with looking at your own inventory. No packets sent, no automation, no
PVP interaction. No ban wave has ever targeted plugins of this profile.

---

## Hacking on it

### Fork and self-host

```bash
cd Bisme/
git init
git add .
git commit -m "Initial commit"
git remote add origin https://github.com/<your-username>/Bisme.git
git push -u origin main
```

Replace `elrichs2` with your username inside `pluginmaster.json` (in
`RepoUrl`, `IconUrl`, and the three `DownloadLink*` fields). Push to main
and the GitHub Actions workflow takes care of the rest: build, package
`latest.zip`, recreate the `latest` release. No commit-back, no merge
conflicts.

### Bumping versions

`bump.ps1` syncs the version across `Bisme.csproj`, `Bisme.json`, and
`pluginmaster.json` in one shot. It works on PowerShell 5.1 (the Windows
default) as well as PowerShell 7+.

```powershell
.\bump.ps1                        # patch  (1.4.0.0 -> 1.4.1.0)
.\bump.ps1 -Patch                 # same
.\bump.ps1 -Minor                 # minor  (1.4.0.0 -> 1.5.0.0)
.\bump.ps1 -Major                 # major  (1.4.0.0 -> 2.0.0.0)
.\bump.ps1 -Version 1.10.0.0      # explicit
.\bump.ps1 -Minor -Changelog "Description of the change"
```

The script reads and writes through explicit UTF-8 BOM-less .NET file
APIs and runs an ASCII-only guard on every manifest before writing.
Non-ASCII bytes (em-dashes, smart quotes, accents, mojibake) abort the
run with a contextual error. This avoids the PS 5.1 default of
ANSI/CP1252 reads, which historically double-encoded the description and
shipped garbled text to users.

A typical release flow:

```powershell
# Edit code
.\bump.ps1 -Minor -Changelog "Added feature X"
git add .
git commit -m "v1.5.0"
git push
```

| Level | When to bump |
|---|---|
| **Major** | Rewrite or breaking change |
| **Minor** | New user-visible feature |
| **Patch** | Bug fix or internal tweak |
| **Build** | Reserved, always 0 |

### Local build

You need the .NET 10 SDK and Dalamud. On Windows with Dalamud installed,
the SDK is auto-detected:

```bash
dotnet build -c Release
```

On Linux or macOS, point at the distrib explicitly:

```bash
export DALAMUD_HOME=/path/to/dalamud-distrib
dotnet build -c Release
```

To dev-test without releasing:

- Copy `bin/Release/Bisme.dll`, `Bisme.json`, and `Bisme.deps.json` into
  `%APPDATA%\XIVLauncher\devPlugins\Bisme\`.
- `/xlplugins`, **Dev Tools**, find Bisme, **Enable**.

### Repository layout

```
.github/workflows/build.yml   Auto build + release on push, ASCII-guarded
Plugin.cs                     Entry point, slash commands, equipped-gear reader
BisData.cs                    Loader for the embedded dataset
Optimizer.cs                  Optimization logic, four-tier scoring, cap-aware
MainWindow.cs                 Full ImGui interface
BisBuddyExport.cs             Serializer for BisBuddy's JSON format
Bisme.csproj                  Project file (.NET 10, Dalamud SDK 15)
Bisme.json                    Internal plugin manifest, synced by bump.ps1
pluginmaster.json             Dalamud repo index, the URL users add
data.json                     Embedded dataset (BiS, items, foods, caps)
bump.ps1                      Version bump script with ASCII guard
global.json                   .NET SDK 10.0.x pin
latest.zip                    Build artifact, rebuilt by CI, gitignored
```

### BisBuddy export shape

The format is reverse-engineered from BisBuddy's `GearsetConverter`. A
minimal export looks like:

```json
{
  "Id": "<guid>",
  "Name": "Bisme - SAM 2026-05-08 12:00",
  "SourceType": 2,
  "ClassJobId": 34,
  "IsActive": true,
  "Priority": 0,
  "ImportDate": "2026-05-08T12:00:00Z",
  "Gearpieces": [
    {
      "ItemId": 49671,
      "IsCollected": false,
      "CollectLock": false,
      "ItemMateria": [
        {"ItemId": 41773, "IsCollected": false, "CollectLock": false},
        {"ItemId": 41773, "IsCollected": false, "CollectLock": false}
      ],
      "PrerequisiteTree": null
    }
  ]
}
```

HQ-able items export as `ItemId + 1_000_000`. NQ-only items (savage,
tome, most shields) keep the raw row ID. The choice is driven by the
`hq` flag in `data.json`, which tracks `canBeHq` from xivgear.

### Data sources

- BiS targets: `staticbis.xivgear.app/<job>/current.json` plus the
  Balance pages for the shortlink UUIDs.
- Items, foods, materia: `data.xivgear.app/Items?job=<JOB>`, `/Food`,
  `/Materia`.
- Cap formula: akhmorning's substat tier table cross-referenced with
  xivgear's `datamanager_new.ts`.
