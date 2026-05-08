<#
bump.ps1 -- Bisme version bumper, compatible PowerShell 5.1+ (Windows par defaut).

Usage:
    .\bump.ps1                              # auto-increment patch (1.4.0.0 -> 1.4.0.1)
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
    - Build: reserve (toujours 0)

Encoding policy:
    - Read/Write JSON manifests as UTF-8 BOM-less via .NET APIs (NOT Get-Content -Raw,
      which defaults to ANSI/CP1252 in PS 5.1 and silently corrupts non-ASCII bytes).
    - Enforce ASCII-only on Description / Punchline / Changelog fields. Any non-ASCII
      char aborts the run rather than risking re-encoding mojibake.
#>
param(
    [string]$Version,
    [switch]$Major,
    [switch]$Minor,
    [switch]$Patch,
    [string]$Changelog = ""
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# UTF-8 sans BOM (instance reutilisable)
$Utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Read-Utf8NoBom([string]$path) {
    return [System.IO.File]::ReadAllText($path, $Utf8NoBom)
}
function Write-Utf8NoBom([string]$path, [string]$content) {
    [System.IO.File]::WriteAllText($path, $content, $Utf8NoBom)
}

# Garde anti-mojibake: refuse tout octet > 0x7F dans le contenu textuel
function Assert-Ascii([string]$path, [string]$content) {
    $bad = [regex]::Matches($content, '[^\x00-\x7F]')
    if ($bad.Count -gt 0) {
        $firstIdx = $bad[0].Index
        $start = [Math]::Max(0, $firstIdx - 30)
        $len = [Math]::Min($content.Length - $start, 80)
        $context = $content.Substring($start, $len)
        Write-Error "ASCII guard: $($bad.Count) non-ASCII char(s) in $path (first @ $firstIdx). Context: ...$context..."
        Write-Error "Use plain ASCII (em-dash -> '--', smart quotes -> '\"', accents -> ASCII)."
        exit 1
    }
}

# Read current version from csproj
$csprojPath = Join-Path $ScriptDir "Bisme.csproj"
$csprojContent = Read-Utf8NoBom $csprojPath
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

# 1. Bisme.csproj
Assert-Ascii $csprojPath $csprojContent
$csprojContent = [regex]::Replace($csprojContent, '<Version>[\d\.]+</Version>', "<Version>$newVer</Version>")
Assert-Ascii $csprojPath $csprojContent
Write-Utf8NoBom $csprojPath $csprojContent
Write-Host "  Bisme.csproj      -> $newVer" -ForegroundColor Green

# 2. Bisme.json -- regex replace, evite ConvertFrom/ConvertTo-Json
$bjPath = Join-Path $ScriptDir "Bisme.json"
$bj = Read-Utf8NoBom $bjPath
Assert-Ascii $bjPath $bj
$bj = [regex]::Replace($bj, '"AssemblyVersion":\s*"[\d\.]+"', '"AssemblyVersion": "' + $newVer + '"')
Assert-Ascii $bjPath $bj
Write-Utf8NoBom $bjPath $bj
Write-Host "  Bisme.json        -> $newVer" -ForegroundColor Green

# 3. pluginmaster.json -- regex replace AssemblyVersion + LastUpdate (+ optionnel Changelog)
$pmPath = Join-Path $ScriptDir "pluginmaster.json"
$pm = Read-Utf8NoBom $pmPath
Assert-Ascii $pmPath $pm
$pm = [regex]::Replace($pm, '"AssemblyVersion":\s*"[\d\.]+"', '"AssemblyVersion": "' + $newVer + '"')
$epoch = [int][double]::Parse((Get-Date -UFormat %s))
$pm = [regex]::Replace($pm, '"LastUpdate":\s*"\d+"', '"LastUpdate": "' + $epoch + '"')
if ($Changelog) {
    $escapedChangelog = $Changelog -replace '\\', '\\' -replace '"', '\"'
    $pm = [regex]::Replace($pm, '"Changelog":\s*"[^"]*"', '"Changelog": "' + $escapedChangelog + '"')
}
Assert-Ascii $pmPath $pm
Write-Utf8NoBom $pmPath $pm
Write-Host "  pluginmaster.json -> $newVer" -ForegroundColor Green

Write-Host ""
Write-Host "Maintenant lance :" -ForegroundColor Cyan
Write-Host "  git add ." -ForegroundColor White
Write-Host "  git commit -m 'v$newVer'" -ForegroundColor White
Write-Host "  git push" -ForegroundColor White
