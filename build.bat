@echo off
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist "%FW%\csc.exe" (
  echo Compilateur csc.exe introuvable dans %WINDIR%\Microsoft.NET.
  if not defined CI pause
  exit /b 1
)
rem Numero de version et adresse des mises a jour, lus dans deux fichiers texte.
if not exist numero-version.txt (
  echo Fichier numero-version.txt introuvable.
  if not defined CI pause
  exit /b 1
)
set "VER="
set /p VER=<numero-version.txt
set "UPD="
if exist mise-a-jour.txt set /p UPD=<mise-a-jour.txt
for /f "tokens=* delims= " %%A in ("%VER%") do set "VER=%%A"
> src\AppVersion.cs echo [assembly: System.Reflection.AssemblyTitle("Dofuxya")] [assembly: System.Reflection.AssemblyProduct("Dofuxya")] [assembly: System.Reflection.AssemblyFileVersion("%VER%")] namespace DofuxyaManager { internal static class AppVersion { public const string Number = "%VER%"; public const string UpdateBase = "%UPD%"; } }
echo Version %VER%

set WM=%WINDIR%\System32\WinMetadata
set "BASE=/nologo /codepage:65001 /target:winexe /out:Dofuxya.exe /win32icon:assets\app.ico /resource:assets\logo-256.png,DofuxyaManager.logo.png @assets\icones.rsp"
set "REFS=/reference:"%FW%\WPF\PresentationFramework.dll" /reference:"%FW%\WPF\PresentationCore.dll" /reference:"%FW%\WPF\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll"
set "WINRT=/define:NOTIF /reference:"%WM%\Windows.UI.winmd" /reference:"%WM%\Windows.Foundation.winmd" /reference:"%WM%\Windows.ApplicationModel.winmd" /reference:"%FW%\System.Runtime.dll" /reference:"%FW%\System.Runtime.WindowsRuntime.dll" /reference:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll""

rem 1er essai : avec la lecture des notifications Windows (suivi du tour sur Retro).
"%FW%\csc.exe" %BASE% %REFS% %WINRT% src\*.cs > build-suivi.log 2>&1
if not errorlevel 1 goto ok
rem Sur le serveur de compilation (GitHub Actions), pas de version au rabais : on s'arrete.
if defined CI (
  type build-suivi.log
  echo La compilation avec le suivi du tour a echoue.
  exit /b 1
)

echo Le suivi du tour par notifications n'a pas pu etre compile sur ce poste.
echo Le detail est dans build-suivi.log : copie son contenu dans le chat.
echo Compilation sans cette fonction...
echo.
"%FW%\csc.exe" %BASE% %REFS% src\*.cs
if errorlevel 1 (
  echo.
  echo La compilation a echoue. Copie les lignes d'erreur ci-dessus dans le chat.
  if not defined CI pause
  exit /b 1
)
echo.
echo OK : Dofuxya.exe est pret, SANS le suivi du tour par notifications.
if not defined CI pause
exit /b 0

:ok
echo OK : Dofuxya.exe est pret dans ce dossier.
if not defined CI pause
