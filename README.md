# Bisme — Dalamud Plugin

Exporte ton **gear équipé + materia + job** en JSON compatible avec
[xivgear.app](https://xivgear.app/) et avec l'app **FFXIV Meld Optimizer**
(`ffxiv_meld_optimizer.html`).

Plugin Dalamud read-only : il lit la mémoire du jeu via `InventoryManager`
et n'envoie **aucun packet**, ne modifie **rien**.

---

## Installation rapide (utilisateur final)

1. Dans le jeu, ouvre `/xlsettings`
2. Onglet **Experimental** → **Custom Plugin Repositories**
3. Ajoute l'URL : `https://raw.githubusercontent.com/elrichs2/Bisme/main/pluginmaster.json`
4. Coche **Enabled** puis clique **Save and Close**
5. `/xlplugins` → Onglet **All Plugins** → recherche **Bisme** → **Install**
6. In-game : `/bisme clipboard` → ouvre l'app HTML → **Importer un gearset** → **Coller** → ✓

---

## Utilisation

```
/bisme            → imprime le JSON dans le chat
/bisme file       → sauvegarde dans Documents/Bisme.json
/bisme clipboard  → copie dans le presse-papier (recommandé)
```

---

## Setup repo (si tu fork ou crées le tien)

### 1. Push sur GitHub

```bash
cd Bisme/
git init
git add .
git commit -m "Initial commit"
git remote add origin https://github.com/YOUR_USERNAME/Bisme.git
git push -u origin main
```

### 2. Replace les placeholders

Dans `pluginmaster.json`, remplace `elrichs2` par ton username GitHub :

```json
"RepoUrl": "https://github.com/ton-username/Bisme",
"DownloadLinkInstall": "https://github.com/ton-username/Bisme/releases/latest/download/latest.zip",
...
```

### 3. GitHub Actions auto-build

Le workflow `.github/workflows/build.yml` est déjà inclus. À chaque push sur `main` :
- Installe .NET 10
- Download Dalamud distrib (latest)
- Build le DLL avec `DALAMUD_HOME` correctement configuré
- Package en `latest.zip`
- Update `pluginmaster.json` avec le timestamp
- Push une release `latest` avec le ZIP attaché

Pour déclencher : push sur `main`, ou crée un tag `v1.0.0`, ou clic manuel
sur **Actions → Build & Release → Run workflow**.

### 4. URL pluginmaster pour Dalamud

Une fois la release créée, ton URL pluginmaster pour Dalamud sera :
```
https://raw.githubusercontent.com/ton-username/Bisme/main/pluginmaster.json
```

---

## Build local (optionnel)

Si tu veux build à la main au lieu de passer par GitHub Actions :

```bash
# Pré-requis: .NET 10 SDK + Dalamud installé via XIVLauncher

# Sur Windows (Dalamud auto-détecté)
dotnet build -c Release

# Sur Linux/Mac avec DALAMUD_HOME explicite
export DALAMUD_HOME=/path/to/dalamud-distrib
dotnet build -c Release
```

Output : `bin/Release/Bisme/Bisme.dll`

Pour test en mode dev :
- Copie le contenu de `bin/Release/` dans `%APPDATA%\XIVLauncher\devPlugins\Bisme\`
- `/xlplugins` → Onglet **Dev Tools** → Bisme → Enable

---

## Structure du repo

```
Bisme/
├── .github/workflows/build.yml   ← GitHub Action auto-build + release
├── Plugin.cs                     ← Code C# principal
├── Bisme.csproj           ← Project file (.NET 10, Dalamud SDK 15)
├── Bisme.json             ← Manifest plugin (interne)
├── pluginmaster.json             ← Index repo Dalamud (template)
├── global.json                   ← Pin .NET SDK 10.0.x
├── latest.zip                    ← Build artifact (recréé par CI)
└── README.md
```

---

## Format de sortie

```json
{
  "name": "MyChar — SAM Live Export",
  "job": "SAM",
  "level": 100,
  "items": {
    "Weapon": { "id": 49671, "materia": [
      {"id": 41773, "locked": false},
      {"id": 41773, "locked": false}
    ]},
    "Head": { "id": 49690, "materia": [...] },
    ...
  },
  "food": 0,
  "timestamp": 1746615600000
}
```

Compatible avec :
- L'import Xivgear (`xivgear.app/?page=importsheet`)
- Le bouton **↙ Importer un gearset** de `ffxiv_meld_optimizer.html`

---

## Limitations

- **Pas de food** exporté : Dalamud n'expose pas l'effet actif de manière stable.
  Choisis ton food directement dans l'app après import.
- Si l'API Dalamud évolue (`IClientState.LocalPlayer`, `Lumina.Excel.Sheets`...),
  ajuste les imports — le plugin est compatible Dalamud SDK 15.
- Slot Waist (idx 5) toujours vide depuis Shadowbringers, conservé par robustesse.

---

## Risque ban

Plugin **read-only** : lit uniquement la mémoire du jeu (équivalent à regarder ton
inventaire). Pas de packets envoyés, pas d'automation, pas d'interaction PVP.
C'est la catégorie la plus sûre dans l'écosystème Dalamud — aucune vague de bans
n'a jamais été observée pour ce type de plugin.
