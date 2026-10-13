param(
    [string]$NewVersion,
    [string]$AssyVersion
)

# ── Patch FileCopy.csproj ─────────────────────────────────────────────────────
$csproj = 'FileCopy.csproj'
$c = [IO.File]::ReadAllText($csproj)
$c = $c -replace '(<Version>)[^<]+(</Version>)',                 "`${1}$NewVersion`${2}"
$c = $c -replace '(<FileVersion>)[^<]+(</FileVersion>)',         "`${1}$NewVersion`${2}"
$c = $c -replace '(<AssemblyVersion>)[^<]+(</AssemblyVersion>)', "`${1}$AssyVersion`${2}"
[IO.File]::WriteAllText($csproj, $c, [Text.Encoding]::UTF8)
Write-Host "  csproj -> $NewVersion"

# ── Patch installer.nsi ───────────────────────────────────────────────────────
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
