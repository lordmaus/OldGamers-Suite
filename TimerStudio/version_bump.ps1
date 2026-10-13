# version_bump.ps1 — Auto-increments yyyy.MM.dd.xxxx in csproj + installer.nsi
# Called by build.bat before dotnet build.
# Outputs the new version string to stdout (captured by build.bat).

param(
    [string]$CsprojPath   = "TimerStudio.csproj",
    [string]$NsiPath      = "installer.nsi",
    [string]$VersionTxtPath = "version.txt",
    [string]$ReadmePath   = "README.md"
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
$csproj = $csproj -replace '<Version>[^<]+</Version>',         "<Version>$newVer</Version>"
$csproj = $csproj -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$newVer</FileVersion>"
$csproj = $csproj -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$asmVer</AssemblyVersion>"
[System.IO.File]::WriteAllText((Resolve-Path $CsprojPath), $csproj)

# ── Patch installer.nsi ───────────────────────────────────────────────────────
$nsi = [System.IO.File]::ReadAllText((Resolve-Path $NsiPath).Path, [System.Text.Encoding]::UTF8)
$nsi = $nsi -replace '(!define APP_VERSION\s+")[^"]+(")' , "`${1}$newVer`${2}"
$nsi = $nsi -replace '(VIProductVersion\s+")[^"]+(")' ,     "`${1}$asmVer`${2}"
[System.IO.File]::WriteAllText((Resolve-Path $NsiPath).Path, $nsi, [System.Text.Encoding]::UTF8)

# ── Patch version.txt ─────────────────────────────────────────────────────────
# S-03: no BOM in version.txt
[System.IO.File]::WriteAllText((Resolve-Path $VersionTxtPath).Path, $newVer, (New-Object System.Text.UTF8Encoding $false))

# ── Patch README.md footer ────────────────────────────────────────────────────
# (dash built via [char]0x2014, not embedded literally -- a raw em-dash
# inside a code token, not just a comment, gets mangled by Windows
# PowerShell 5.1 reading this file without a UTF-8 BOM)
$dash    = [char]0x2014
$readme  = [System.IO.File]::ReadAllText((Resolve-Path $ReadmePath).Path, [System.Text.Encoding]::UTF8)
$reReplacePattern = '(?m)^\*\*[0-9]{4}\.[0-9]{1,2}\.[0-9]{1,2}\.[0-9]+\*\* ' + $dash + ' Windows'
$readme  = $readme -replace $reReplacePattern, ("**$newVer** " + $dash + " Windows")
[System.IO.File]::WriteAllText((Resolve-Path $ReadmePath).Path, $readme, [System.Text.Encoding]::UTF8)

# ── Sync check — fail loudly if csproj/installer/version.txt/README ever drift ─
$reReadVer = [regex]::Match((Get-Content $CsprojPath -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
$reReadTxt = (Get-Content $VersionTxtPath -Raw).Trim()
$reReadMd  = [regex]::Match((Get-Content $ReadmePath -Raw), ('\*\*([0-9.]+)\*\* ' + $dash + ' Windows')).Groups[1].Value
if ($reReadVer -ne $newVer -or $reReadTxt -ne $newVer -or $reReadMd -ne $newVer) {
    throw "Version sync check failed after bump: csproj=$reReadVer version.txt=$reReadTxt README=$reReadMd expected=$newVer"
}

# ── Output new version (captured by build.bat) ────────────────────────────────
Write-Host $newVer
