<#
bump.ps1 — Bisme version bumper, compatible PowerShell 5.1+ (Windows par défaut).

Usage:
    .\bump.ps1                              # auto-incrément patch (1.4.0.0 -> 1.4.0.1)
    .\bump.ps1 -Patch                       # idem
    .\bump.ps1 -Minor                       # bump minor (1.4.0.0 -> 1.5.0.0)
    .\bump.ps1 -Major                       # bump major (1.4.0.0 -> 2.0.0.0)
    .\bump.ps1 -Version 1.5.2.0             # version explicite
    .\bump.ps1 -Minor -Changelog "feature"  # avec changelog

Versioning convention:
    Major.Minor.Patch.Build
    - Major: rewrite, breaking change
    - Minor: nouvelle feature
    - Patch: bug fix / tweak
    - Build: réservé (toujours 0)
#>
param(
    [string]$Version,
    [switch]$Major,
    [switch]$Minor,
    [switch]$Patch,
    [string]$Changelog = ""
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Read current version from csproj
$csprojPath = Join-Path $ScriptDir "Bisme.csproj"
$csprojContent = Get-Content $csprojPath -Raw
$match = [regex]::Match($csprojContent, '<Version>(\d+)\.(\d+)\.(\d+)\.(\d+)</Version>')
if (-not $match.Success) {
    Write-Error "Impossible de lire la version actuelle depuis Bisme.csproj"
    exit 1
}

$curMajor = [int]$match.Groups[1].Value
$curMinor = [int]$match.Groups[2].Value
$curPatch = [int]$match.Groups[3].Value
$curBuild = [int]$match.Groups[4].Value
$current = "$curMajor.$curMinor.$curPatch.$curBuild"

# Determine new version
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        Write-Error "Version doit etre au format X.Y.Z.W (ex: 1.5.0.0)"
        exit 1
    }
    $newVer = $Version
}
elseif ($Major) {
    $newVer = "$($curMajor + 1).0.0.0"
}
elseif ($Minor) {
    $newVer = "$curMajor.$($curMinor + 1).0.0"
}
else {
    # Default: patch bump
    $newVer = "$curMajor.$curMinor.$($curPatch + 1).0"
}

Write-Host "Current: $current  ->  New: $newVer" -ForegroundColor Cyan
Write-Host ""

# UTF-8 sans BOM, compatible PS 5.1+
function Write-Utf8NoBom([string]$path, [string]$content) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
}

# 1. Bisme.csproj
$csprojContent = [regex]::Replace($csprojContent, '<Version>[\d\.]+</Version>', "<Version>$newVer</Version>")
Write-Utf8NoBom $csprojPath $csprojContent
Write-Host "  Bisme.csproj      -> $newVer" -ForegroundColor Green

# 2. Bisme.json — regex replace, evite ConvertFrom/ConvertTo-Json
$bjPath = Join-Path $ScriptDir "Bisme.json"
$bj = Get-Content $bjPath -Raw
$bj = [regex]::Replace($bj, '"AssemblyVersion":\s*"[\d\.]+"', '"AssemblyVersion": "' + $newVer + '"')
Write-Utf8NoBom $bjPath $bj
Write-Host "  Bisme.json        -> $newVer" -ForegroundColor Green

# 3. pluginmaster.json — regex replace AssemblyVersion + LastUpdate (+ optionnel Changelog)
$pmPath = Join-Path $ScriptDir "pluginmaster.json"
$pm = Get-Content $pmPath -Raw
$pm = [regex]::Replace($pm, '"AssemblyVersion":\s*"[\d\.]+"', '"AssemblyVersion": "' + $newVer + '"')
$epoch = [int][double]::Parse((Get-Date -UFormat %s))
$pm = [regex]::Replace($pm, '"LastUpdate":\s*"\d+"', '"LastUpdate": "' + $epoch + '"')
if ($Changelog) {
    $escapedChangelog = $Changelog -replace '\\', '\\' -replace '"', '\"'
    $pm = [regex]::Replace($pm, '"Changelog":\s*"[^"]*"', '"Changelog": "' + $escapedChangelog + '"')
}
Write-Utf8NoBom $pmPath $pm
Write-Host "  pluginmaster.json -> $newVer" -ForegroundColor Green

Write-Host ""
Write-Host "Maintenant lance :" -ForegroundColor Cyan
Write-Host "  git add ." -ForegroundColor White
Write-Host "  git commit -m 'v$newVer'" -ForegroundColor White
Write-Host "  git push" -ForegroundColor White
