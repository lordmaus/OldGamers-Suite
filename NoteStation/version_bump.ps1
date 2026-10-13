# version_bump.ps1 — Auto-increments yyyy.MM.dd.xxxx in csproj + installer.nsi
# Called by build.bat before dotnet build.
# Outputs the new version string to stdout (captured by build.bat).

param(
    [string]$CsprojPath = "NoteStation.csproj",
    [string]$NsiPath    = "installer.nsi"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Today's date prefix ───────────────────────────────────────────────────────
$today = (Get-Date).ToString("yyyy.MM.dd")

# ── Read current version from csproj ─────────────────────────────────────────
$csproj = Get-Content $CsprojPath -Raw
$curVer = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $curVer) { throw "Could not find <Version> in $CsprojPath" }

# ── Compute new counter ───────────────────────────────────────────────────────
$parts   = $curVer -split '\.'
$curDate = "$($parts[0]).$($parts[1]).$($parts[2])"
$ctr     = if ($curDate -eq $today) { [int]$parts[3] + 1 } else { 1 }

$newVer  = "$today.$($ctr.ToString('D4'))"

# AssemblyVersion strips leading zeros: yyyy.M.d.N
$d      = [datetime]::ParseExact($today, "yyyy.MM.dd", $null)
$asmVer = "$($d.Year).$($d.Month).$($d.Day).$ctr"

# ── Patch csproj ─────────────────────────────────────────────────────────────
$csproj = $csproj -replace '<Version>[^<]+</Version>',              "<Version>$newVer</Version>"
$csproj = $csproj -replace '<FileVersion>[^<]+</FileVersion>',      "<FileVersion>$newVer</FileVersion>"
$csproj = $csproj -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$asmVer</AssemblyVersion>"
[System.IO.File]::WriteAllText((Resolve-Path $CsprojPath), $csproj)

# ── Patch installer.nsi ───────────────────────────────────────────────────────
if (Test-Path $NsiPath) {
    $nsi = [System.IO.File]::ReadAllText((Resolve-Path $NsiPath).Path, [System.Text.Encoding]::UTF8)
    $nsi = $nsi -replace '(!define APP_VERSION\s+")[^"]+(")' , "`${1}$newVer`${2}"
    $nsi = $nsi -replace '(VIProductVersion\s+")[^"]+(")' ,     "`${1}$asmVer`${2}"
    [System.IO.File]::WriteAllText((Resolve-Path $NsiPath).Path, $nsi, [System.Text.Encoding]::UTF8)
}

# ── Update version.txt ────────────────────────────────────────────────────────
# UTF-8 without BOM (Out-File -Encoding UTF8 on Windows PowerShell 5.1 writes a BOM — S-03).
[System.IO.File]::WriteAllText((Join-Path (Get-Location) "version.txt"), $newVer, (New-Object System.Text.UTF8Encoding($false)))

# ── Output new version (captured by build.bat) ────────────────────────────────
Write-Host $newVer
