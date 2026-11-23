# Tests de la lecture d'ecran : chaque capture de tests\captures est lue par Dofuxya.exe
# (mode --essai-lecture, sans fenetre) et le resultat est compare a tests\attendu.txt.
#   powershell -File tests\verifier-lecture.ps1              verifie (code 1 en cas d'ecart)
#   powershell -File tests\verifier-lecture.ps1 -MettreAJour  reecrit attendu.txt (apres verification a l'oeil)
# Dofuxya.exe doit etre compile avant (build.bat).
param([switch]$MettreAJour)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$expected = Join-Path $PSScriptRoot "attendu.txt"
$images = @(Get-ChildItem (Join-Path $PSScriptRoot "captures") -Filter *.png | Sort-Object Name)
if ($images.Count -eq 0) {
  Write-Output "Aucune capture dans tests\captures : rien a verifier."
  exit 0
}
$exe = Join-Path $root "Dofuxya.exe"
if (-not (Test-Path $exe)) { throw "Dofuxya.exe introuvable : lancer build.bat d'abord." }

$out = Join-Path ([IO.Path]::GetTempPath()) "dofuxya-essai-lecture.txt"
if (Test-Path $out) { Remove-Item $out -Force }
$arguments = @("--essai-lecture", "`"$out`"") + ($images | ForEach-Object { "`"$($_.FullName)`"" })
$process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0 -or -not (Test-Path $out)) { throw "La lecture des captures a echoue (code $($process.ExitCode))." }
$actual = (Get-Content $out -Raw -Encoding UTF8).Replace("`r`n", "`n").TrimEnd()
# L'heure du releve change a chaque passage : remplacee par un repere fixe.
$actual = $actual -replace "relev\S+ \S+ \d{1,2}:\d{2}", "releve a HH:MM"

if ($MettreAJour) {
  [IO.File]::WriteAllText($expected, $actual + "`n", (New-Object Text.UTF8Encoding $false))
  Write-Output "attendu.txt mis a jour ($($images.Count) captures)."
  exit 0
}
if (-not (Test-Path $expected)) { throw "tests\attendu.txt absent : lancer le script avec -MettreAJour." }
$wanted = (Get-Content $expected -Raw -Encoding UTF8).Replace("`r`n", "`n").TrimEnd()
if ($actual -eq $wanted) {
  Write-Output "Lecture d'ecran : $($images.Count) captures, resultats identiques."
  exit 0
}
Write-Output "Lecture d'ecran : ecart avec attendu.txt"
Compare-Object ($wanted -split "`n") ($actual -split "`n") | ForEach-Object {
  $side = if ($_.SideIndicator -eq "<=") { "attendu" } else { "lu     " }
  Write-Output "  $side : $($_.InputObject)"
}
exit 1
