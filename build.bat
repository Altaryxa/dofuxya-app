@echo off
setlocal
cd /d "%~dp0"
rem Compile Dofuxya.exe avec le compilateur C# fourni avec Windows (.NET Framework 4) : rien a installer.
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
rem Lecture des notifications Windows (suivi du tour sur Retro).
set "WINRT=/reference:"%WM%\Windows.UI.winmd" /reference:"%WM%\Windows.Foundation.winmd" /reference:"%WM%\Windows.ApplicationModel.winmd" /reference:"%FW%\System.Runtime.dll" /reference:"%FW%\System.Runtime.WindowsRuntime.dll" /reference:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll""

"%FW%\csc.exe" %BASE% %REFS% %WINRT% src\*.cs
if errorlevel 1 (
  echo.
  echo La compilation a echoue.
  if not defined CI pause
  exit /b 1
)
echo OK : Dofuxya.exe est pret dans ce dossier.
if not defined CI pause
