# bump_version.ps1 - suite version bump (S-03, 2026-10-14). Called by Tools\bump_version.cmd.
#
# version.txt = one line "yyyy.MM.dd.NNNN" (UTF-8, no BOM, no newline).
#   same local day  -> NNNN + 1
#   new day/missing -> today.0001
# -NoBump only reads and validates.
#
# Stdout: "<yyyy.MM.dd.NNNN> <Y.M.D.N>" (padded + unpadded numeric form for VIProductVersion).

param(
    [Parameter(Mandatory = $true)][string]$File,
    [switch]$NoBump
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture

$current = if (Test-Path -LiteralPath $File) { [IO.File]::ReadAllText($File).Trim() } else { '' }
# Legacy "yyyy.MM.dd NNNN" and 3-digit counters accepted on read.
$m = [regex]::Match($current, '^(\d{4}\.\d{2}\.\d{2})[. ](\d{1,4})$')

if ($NoBump) {
    if (-not $m.Success) { throw "version.txt is not 'yyyy.MM.dd.NNNN': '$current' ($File)" }
    $date = $m.Groups[1].Value
    $n    = [int]$m.Groups[2].Value
}
else {
    $date = (Get-Date).ToString('yyyy.MM.dd', $inv)
    $n    = if ($m.Success -and $m.Groups[1].Value -eq $date) { [int]$m.Groups[2].Value + 1 } else { 1 }
    if ($n -gt 9999) { throw "More than 9999 builds on $date." }
}

$version = '{0}.{1}' -f $date, $n.ToString('D4', $inv)
if (-not $NoBump) {
    $tmp = "$File.tmp"
    [IO.File]::WriteAllText($tmp, $version, (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $File -Force
}

$d = [datetime]::ParseExact($date, 'yyyy.MM.dd', $inv)
Write-Output ('{0} {1}.{2}.{3}.{4}' -f $version, $d.Year, $d.Month, $d.Day, $n)
