@echo off
setlocal
cd /d "%~dp0"
rem Fabrique l'installateur publication\Dofuxya-Setup-x.y.z.exe avec Inno Setup.
rem Lance par la publication automatique (.github\workflows\release.yml), apres build.bat.

if not exist Dofuxya.exe (
  echo Compile d'abord l'outil avec build.bat.
  if not defined CI pause
  exit /b 1
)
set "VER="
set /p VER=<numero-version.txt
for /f "tokens=* delims= " %%A in ("%VER%") do set "VER=%%A"

rem Inno Setup 7 est prefere, Inno Setup 6 est accepte.
set "ISCC="
for %%V in (6 7) do (
  if exist "%LOCALAPPDATA%\Programs\Inno Setup %%V\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup %%V\ISCC.exe"
  if exist "%ProgramFiles(x86)%\Inno Setup %%V\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup %%V\ISCC.exe"
  if exist "%ProgramFiles%\Inno Setup %%V\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup %%V\ISCC.exe"
)
if not defined ISCC (
  echo Inno Setup introuvable.
  if not defined CI pause
  exit /b 1
)

if not exist publication mkdir publication
"%ISCC%" /Q "/DAppVer=%VER%" "/O%CD%\publication" installer\dofuxya.iss
if errorlevel 1 (
  echo La fabrication de l'installateur a echoue.
  if not defined CI pause
  exit /b 1
)
echo OK : publication\Dofuxya-Setup-%VER%.exe
if not defined CI pause
