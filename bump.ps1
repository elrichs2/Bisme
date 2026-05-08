# bump.ps1 — bump version everywhere in one command
# Usage: .\bump.ps1 1.3.0.0  (or any X.Y.Z.W)
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$Changelog = ""
)

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    Write-Error "Version must be in X.Y.Z.W format (e.g. 1.3.0.0)"
    exit 1
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Bisme.csproj
$csproj = Get-Content "$ScriptDir\Bisme.csproj" -Raw
$csproj = $csproj -replace '<Version>[\d\.]+</Version>', "<Version>$Version</Version>"
Set-Content "$ScriptDir\Bisme.csproj" $csproj -Encoding utf8NoBOM
Write-Host "Bisme.csproj   -> $Version" -ForegroundColor Green

# Bisme.json
$bj = Get-Content "$ScriptDir\Bisme.json" -Raw | ConvertFrom-Json
$bj.AssemblyVersion = $Version
$bj | ConvertTo-Json -Depth 5 | Set-Content "$ScriptDir\Bisme.json" -Encoding utf8NoBOM
Write-Host "Bisme.json     -> $Version" -ForegroundColor Green

# pluginmaster.json (must stay an array)
$pm = Get-Content "$ScriptDir\pluginmaster.json" -Raw | ConvertFrom-Json
if ($pm -isnot [System.Array]) { $pm = @($pm) }
$pm[0].AssemblyVersion = $Version
$pm[0].LastUpdate = "$([int][double]::Parse((Get-Date -UFormat %s)))"
if ($Changelog) { $pm[0].Changelog = $Changelog }
$pm | ConvertTo-Json -Depth 5 -AsArray | Set-Content "$ScriptDir\pluginmaster.json" -Encoding utf8NoBOM
Write-Host "pluginmaster.json -> $Version" -ForegroundColor Green

Write-Host ""
Write-Host "Now run: git add . && git commit -m 'v$Version' && git push" -ForegroundColor Cyan
