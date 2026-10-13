param(
    [string]$NewVersion,
    [string]$AssyVersion
)

# ── Patch ReticleOverlay.csproj ───────────────────────────────────────────────
$csproj = 'ReticleOverlay.csproj'
$c = [IO.File]::ReadAllText($csproj)
$c = $c -replace '(<Version>)[^<]+(</Version>)',         "`${1}$NewVersion`${2}"
$c = $c -replace '(<FileVersion>)[^<]+(</FileVersion>)',  "`${1}$NewVersion`${2}"
$c = $c -replace '(<AssemblyVersion>)[^<]+(</AssemblyVersion>)', "`${1}$AssyVersion`${2}"
[IO.File]::WriteAllText($csproj, $c, [Text.Encoding]::UTF8)
Write-Host "  csproj -> $NewVersion"

# ── Patch installer.nsi ───────────────────────────────────────────────────────
# Line format:  !define APP_VERSION   "2026.4.12.001"
# Use simple string replace — no regex needed, exact match is safer here.
$nsi = 'installer.nsi'
$lines = [IO.File]::ReadAllLines($nsi)
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match '^!define APP_VERSION') {
        $lines[$i] = "!define APP_VERSION   `"$NewVersion`""
    }
    if ($line -match '^VIProductVersion') {
        $lines[$i] = "VIProductVersion `"$AssyVersion`""
    }
}
[IO.File]::WriteAllLines($nsi, $lines, [Text.Encoding]::UTF8)
Write-Host "  nsi    -> $NewVersion"
