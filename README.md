# Bisme — Dalamud Plugin

**Optimiseur de melds in-game pour FFXIV.** Choisis un job, charge ton stuff équipé,
clique sur Auto-Optimize : le plugin pick la meilleure food et place les materia
pour matcher la cible BiS, en respectant le cap par pièce.

Plugin Dalamud read-only (catégorie la plus sûre) : il lit la mémoire du jeu via
`InventoryManager`, n'envoie aucun packet, ne modifie rien dans le jeu.

![Patch 7.4 — Heavyweight Savage / i790 BiS data embedded](https://img.shields.io/badge/patch-7.4-orange) ![DalamudApiLevel 15](https://img.shields.io/badge/api-15-blue)

---

## Features

- **UI ImGui complète** in-game : sélecteurs job + food + 11 slots de gear avec materia, panneau de stats vs BiS target avec deltas colorés
- **21 jobs** supportés (tanks/heals/melee/ranged/casters), data BiS i790 embed
- **Auto-Optimize** : pick la meilleure food + place les materia en 4 tiers de scoring (no overcap > overcap partiel > overshoot > capped)
- **Cap par pièce** respecté : `ItemLevel.SubStatCap × ratio_slot`, vrais grades XII/XI selon slot (base + 1er overmeld = XII, slots avancés = XI)
- **Auto-detect changement de job** : switch de class in-game = sync auto de l'optimizer + reload du gear équipé
- **Send to BisBuddy** : export en un clic vers BisBuddy pour highlighting des items à farmer (avec HQ flag respecté)
- **Charge le gear équipé** : lit ton inventaire courant via `InventoryManager` (items + materia + grades)

---

## Installation

1. In-game, tape `/xlsettings`
2. Onglet **Experimental** → **Custom Plugin Repositories**
3. Ajoute l'URL : `https://raw.githubusercontent.com/elrichs2/Bisme/main/pluginmaster.json`
4. Coche **Enabled** → **Save and Close**
5. `/xlplugins` → **All Plugins** → cherche **Bisme** → **Install**

---

## Utilisation

### Slash commands

```
/bisme            → toggle la fenêtre Bisme
/bisme load       → ouvre + charge le gear équipé
/bisme optimize   → ouvre + charge équipé + auto-optimize en un coup
```

### Workflow type

1. `/bisme load` → la fenêtre s'ouvre avec ton job courant + ton stuff équipé pré-chargé
2. Clic **Auto-Optimize Materia** → l'optimizer pick la food et place les melds optimaux
3. Vérifie le panneau **Stats vs BiS Target** (deltas verts ≤ ±27 = OK)
4. Clic **Send to BisBuddy** → le JSON est dans ton presse-papier
5. `/bisbuddy` → **Add Gearset** → **JSON** → **Ctrl+V** → Import
6. BisBuddy highlight maintenant les items à farmer dans loot/shops/marketboard/melding

### Switch de job in-game

Aucune action requise. Quand tu changes de class, le plugin sync l'optimizer
sur le nouveau job et recharge ton gear équipé en arrière-plan (~0.15s de délai
le temps que l'inventaire se settle).

---

## Pour les développeurs (fork ou self-host)

### Setup repo

```bash
cd Bisme/
git init
git add .
git commit -m "Initial commit"
git remote add origin https://github.com/<ton-username>/Bisme.git
git push -u origin main
```

Remplace `elrichs2` par ton username dans `pluginmaster.json` (les `RepoUrl`,
`IconUrl`, `DownloadLink*`).

### Bump version : `bump.ps1`

Script PowerShell (compatible PS 5.1+ Windows par défaut) qui synchronise la
version dans les 3 fichiers en une commande :

```powershell
.\bump.ps1                       # auto-bump patch  (1.4.0.0 -> 1.4.1.0)
.\bump.ps1 -Patch                # idem
.\bump.ps1 -Minor                # bump minor       (1.4.0.0 -> 1.5.0.0)
.\bump.ps1 -Major                # bump major       (1.4.0.0 -> 2.0.0.0)
.\bump.ps1 -Version 1.10.0.0     # version explicite
.\bump.ps1 -Minor -Changelog "Description des changements"
```

Le script met à jour `Bisme.csproj`, `Bisme.json` et `pluginmaster.json` avec
la même version, le timestamp `LastUpdate`, et optionnellement le changelog.

Convention de versioning :

| Niveau | Quand bump | Exemple |
|---|---|---|
| **Major** | Breaking change, rewrite | 1.x → 2.0.0.0 |
| **Minor** | Nouvelle feature visible | 1.4.0.0 → 1.5.0.0 |
| **Patch** | Bug fix, tweak interne | 1.4.0.0 → 1.4.1.0 |
| **Build** | Réservé (toujours 0) | — |

### Workflow type pour publier une mise à jour

```powershell
# 1. Modifie le code
# 2. Bump
.\bump.ps1 -Minor -Changelog "Ajout de la feature X"
# 3. Push
git add .
git commit -m "v1.5.0"
git push
```

GitHub Actions s'occupe du reste : build → package `latest.zip` → release.
Aucun commit-back, aucun conflit.

### Build local (optionnel)

```bash
# Pré-requis : .NET 10 SDK + Dalamud (auto-détecté sur Windows si Dalamud installé)
dotnet build -c Release

# Linux/Mac : pointer vers Dalamud distrib explicitement
export DALAMUD_HOME=/path/to/dalamud-distrib
dotnet build -c Release
```

Pour tester en mode dev sans passer par la release :
- Copie `bin/Release/Bisme.dll` + `Bisme.json` + `Bisme.deps.json` dans
  `%APPDATA%\XIVLauncher\devPlugins\Bisme\`
- `/xlplugins` → onglet **Dev Tools** → Bisme → Enable

---

## Structure du repo

```
Bisme/
├── .github/workflows/build.yml   ← Auto-build + release sur push
├── Plugin.cs                     ← Entry point + slash commands + lecture gear équipé
├── BisData.cs                    ← Loader du dataset embed (BiS targets, items, foods)
├── Optimizer.cs                  ← Logique d'optimisation (4-tier scoring, cap-aware)
├── MainWindow.cs                 ← UI ImGui complète
├── BisBuddyExport.cs             ← Sérialiseur vers le format JSON BisBuddy (HQ-aware)
├── Bisme.csproj                  ← Project file (.NET 10, Dalamud SDK 15)
├── Bisme.json                    ← Manifest plugin (interne, version sync via bump.ps1)
├── pluginmaster.json             ← Index repo Dalamud (URL d'install)
├── data.json                     ← Dataset embedded (BiS i790 + 1673 items + foods + caps)
├── bump.ps1                      ← Script de bump version
├── global.json                   ← Pin .NET SDK 10.0.x
└── latest.zip                    ← Build artifact (recréé par CI, .gitignore-ignored)
```

---

## Format JSON de l'export BisBuddy

Le bouton **Send to BisBuddy** sérialise le state Bisme dans le format JSON
attendu par le `JsonSource` de BisBuddy (rétro-engineering du `GearsetConverter`) :

```json
{
  "Id": "<guid>",
  "Name": "Bisme — SAM 2026-05-08 12:00",
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

Les items HQ-able (gear craft) ont leur `ItemId + 1_000_000` selon la convention
BisBuddy. Les items savage / tome (NQ-only) restent à l'ID brut.

---

## Limitations connues

- **Food active non lue** : Dalamud n'expose pas l'effet food courant de
  manière stable. Tu choisis la food directement dans l'UI Bisme.
- **Slot Waist** (index 5) : retiré depuis Shadowbringers, le mapping le saute.
- **Pas de support PLD shield offhand** : le slot OffHand n'est pas modélisé
  (PLD weapon = épée + bouclier comme un seul item dans l'optimizer).

---

## Risque ban

Plugin **read-only** : lit uniquement la mémoire du jeu (équivalent à regarder
ton inventaire). Aucun packet envoyé, aucune automation, aucune interaction
PVP. C'est la catégorie la plus sûre dans l'écosystème Dalamud. Aucune vague
de bans n'a jamais été observée sur ce profil de plugin.
