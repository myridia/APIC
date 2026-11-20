$ErrorActionPreference = 'Stop'
$pfx = '__PFX__'
$exe = '__EXE__'

$roots = @(
  'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe',
  'C:\Program Files\Windows Kits\10\bin\*\x64\signtool.exe'
)
$signtool = $null
foreach ($r in $roots) {
  $found = Get-ChildItem -Path $r -ErrorAction SilentlyContinue |
           Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
  if ($found) { $signtool = $found; break }
}
if (-not $signtool) { throw 'signtool.exe not found in Windows Kits (install Windows SDK)' }

if (-not (Test-Path -LiteralPath $pfx)) {
  $candidates = @(
    "$env:USERPROFILE",
    "$env:USERPROFILE\Desktop",
    "$env:USERPROFILE\Documents",
    "$env:USERPROFILE\Downloads"
  )
  $found = $null
  foreach ($d in $candidates) {
    if (-not (Test-Path -LiteralPath $d)) { continue }
    $found = Get-ChildItem -Path $d -Filter 'MySPC.pfx' -Recurse -Depth 2 -ErrorAction SilentlyContinue |
             Select-Object -First 1 -ExpandProperty FullName
    if ($found) { break }
  }
  if ($found) {
    Write-Host "PFX not at $pfx - using found file: $found"
    $pfx = $found
  } else {
    throw "PFX not found: $pfx (searched $($candidates -join ', '))"
  }
}
if (-not (Test-Path -LiteralPath $exe)) { throw "EXE not found: $exe" }

Write-Host "Using signtool: $signtool"
Write-Host "Using PFX: $pfx"
& $signtool sign /v /f $pfx /tr http://timestamp.digicert.com /td sha256 /fd sha256 $exe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '--- verify (informational) ---'
& $signtool verify /pa $exe
Write-Host "verify exit code: $LASTEXITCODE"
exit 0
