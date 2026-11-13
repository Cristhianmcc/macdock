# Compila MacDock con el compilador de C# que ya trae Windows (.NET Framework 4.x). No necesita instalar nada.
# Uso:  powershell -ExecutionPolicy Bypass -File build.ps1
# Resultado: dist\  (Instalar MacDock.exe, Desinstalar MacDock.exe, LEEME.txt, licencia de Inter)
#            descargas\MacDock-Instalador.zip

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$build = Join-Path $root "build"
$dist = Join-Path $root "dist"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "No se encontro el compilador de C# de .NET Framework 4 ($csc)." }

New-Item -ItemType Directory -Force $build, $dist | Out-Null
$cs = "Native.cs", "Dock.cs", "Setup.cs", "Saver.cs", "Stack.cs", "MenuBar.cs", "Ui.cs", "FileOps.cs", "QuickLook.cs", "Spotlight.cs", "Finder.cs", "MacButtons.cs", "ShellMenu.cs", "AppIndex.cs", "Launchpad.cs", "AppSwitcher.cs", "Widgets.cs", "Wallpapers.cs", "ActivityMonitor.cs", "TrayApps.cs", "NotificationCenter.cs", "NowPlaying.cs", "Focus.cs", "MacWindow.cs", "MacChrome.cs", "FocusWindow.cs", "ClockApp.cs" | ForEach-Object { Join-Path $src $_ }
$res = @(
    "/resource:$src\ajustes.ps1,ajustes.ps1",
    "/resource:$src\fonts\Inter-Regular.ttf,Inter-Regular.ttf",
    "/resource:$src\fonts\Inter-Medium.ttf,Inter-Medium.ttf",
    "/resource:$src\fonts\Inter-SemiBold.ttf,Inter-SemiBold.ttf",
    "/resource:$src\fonts\OFL.txt,OFL.txt")
# --- Modulo opcional de musica (API oficial de Windows para "que esta sonando"). Si no se puede compilar
#     en esta PC, MacDock se compila igual y usa el metodo anterior (titulo de la ventana y teclas multimedia).
$mediaDll = Join-Path $build "MacDockMedia.dll"
Remove-Item $mediaDll -ErrorAction SilentlyContinue
try {
    $wm = Join-Path $env:WINDIR "System32\WinMetadata"
    $fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
    $gac = Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL"
    $facades = "System.Runtime", "System.Threading.Tasks", "System.IO", "System.Runtime.InteropServices.WindowsRuntime" | ForEach-Object {
        Get-ChildItem (Join-Path $gac $_) -Recurse -Filter "$_.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if ((Test-Path "$wm\Windows.Media.winmd") -and (Test-Path "$fw\System.Runtime.WindowsRuntime.dll") -and $facades.Count -gt 0) {
        $margs = @("/nologo", "/target:library", "/optimize+", "/out:$mediaDll",
                   "/r:$wm\Windows.Foundation.winmd", "/r:$wm\Windows.Media.winmd", "/r:$wm\Windows.Storage.winmd",
                   "/r:$fw\System.Runtime.WindowsRuntime.dll") + ($facades | ForEach-Object { "/r:$($_.FullName)" }) + @((Join-Path $src "media\MediaSession.cs"))
        & $csc @margs | Out-Host
    }
} catch { }
if ((Test-Path $mediaDll) -and $LASTEXITCODE -eq 0) {
    $res += "/resource:$mediaDll,MacDockMedia.dll"
    Write-Host "Musica: usando la API de Windows (titulo, artista y portada)." -ForegroundColor Cyan
} else {
    Write-Host "Aviso: sin el modulo de musica de Windows; se usa el titulo de la ventana." -ForegroundColor Yellow
}

$out = Join-Path $build "MacDock.exe"
$args2 = @("/nologo", "/target:winexe", "/platform:x64", "/optimize+", "/win32icon:$src\dock.ico", "/win32manifest:$src\app.manifest") + $res +
         @("/out:$out", "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", "/r:System.Data.dll", "/r:System.Core.dll", "/r:Microsoft.CSharp.dll") + $cs
& $csc @args2
if ($LASTEXITCODE -ne 0) { throw "La compilacion fallo." }

Copy-Item $out (Join-Path $dist "MacDock.exe") -Force
Copy-Item $out (Join-Path $dist "Instalar MacDock.exe") -Force
Copy-Item $out (Join-Path $dist "Desinstalar MacDock.exe") -Force
Copy-Item (Join-Path $src "LEEME.txt") $dist -Force
Copy-Item (Join-Path $src "fonts\OFL.txt") (Join-Path $dist "Inter-licencia-OFL.txt") -Force

$zipDir = Join-Path $root "descargas"; New-Item -ItemType Directory -Force $zipDir | Out-Null
$zip = Join-Path $zipDir "MacDock-Instalador.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $dist "*") -DestinationPath $zip
Write-Host "Listo: $zip" -ForegroundColor Green
