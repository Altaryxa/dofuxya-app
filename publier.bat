@echo off
setlocal
cd /d "%~dp0"
rem Fabrique l'installateur et le fichier de version a deposer sur le site.

if not exist Dofuxya.exe (
  echo Compile d'abord l'outil avec build.bat.
  if not defined CI pause
  exit /b 1
)
set "VER="
set /p VER=<numero-version.txt
for /f "tokens=* delims= " %%A in ("%VER%") do set "VER=%%A"
set "UPD="
if exist mise-a-jour.txt set /p UPD=<mise-a-jour.txt
if /i not "%UPD:~0,8%"=="https://" (
  echo Indique d'abord l'adresse de ton site dans mise-a-jour.txt, en https.
  echo Exemple : https://monsite.fr/dofuxya/
  if not defined CI pause
  exit /b 1
)
if not "%UPD:~-1%"=="/" set "UPD=%UPD%/"

rem Inno Setup 7 (64 ou 32 bits) est prefere, Inno Setup 6 est accepte.
set "ISCC="
for %%V in (6 7) do (
  if exist "%LOCALAPPDATA%\Programs\Inno Setup %%V\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup %%V\ISCC.exe"
  if exist "%ProgramFiles(x86)%\Inno Setup %%V\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup %%V\ISCC.exe"
  if exist "%ProgramFiles%\Inno Setup %%V\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup %%V\ISCC.exe"
)
if not defined ISCC (
  echo Inno Setup introuvable. Installe-le depuis https://jrsoftware.org/isdl.php puis relance.
  if not defined CI pause
  exit /b 1
)

if not exist publication mkdir publication
set "SETUP=Dofuxya-Setup-%VER%.exe"
echo Compilateur : %ISCC%
"%ISCC%" /Q "/DAppVer=%VER%" "/O%CD%\publication" installer\dofuxya.iss
if errorlevel 1 (
  echo La fabrication de l'installateur a echoue.
  if not defined CI pause
  exit /b 1
)

set "HASH="
for /f %%H in ('powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 -LiteralPath 'publication\%SETUP%').Hash"') do set "HASH=%%H"
if not defined HASH (
  echo Calcul de l'empreinte impossible.
  if not defined CI pause
  exit /b 1
)

rem Copie sous un nom fixe, pour le lien de telechargement de ton site.
copy /y "publication\%SETUP%" "publication\Dofuxya-Setup.exe" > nul

> publication\version.txt echo version=%VER%
>> publication\version.txt echo url=%UPD%%SETUP%
>> publication\version.txt echo sha256=%HASH%

echo.
echo Pret. Depose sur ton site, dans %UPD% :
echo   1. publication\%SETUP%
echo   2. publication\Dofuxya-Setup.exe  (le lien de telechargement de ta page, toujours le meme)
echo   3. publication\version.txt        (en dernier, il declenche la mise a jour chez les joueurs)
echo.
echo Lien de telechargement a mettre sur ta page : %UPD%Dofuxya-Setup.exe
echo.
if not defined CI pause
