# Compila MacDock con el compilador de C# que ya trae Windows (.NET Framework 4.x). No necesita instalar nada.
#
# Uso normal (compila sin firmar):
#   powershell -ExecutionPolicy Bypass -File build.ps1
#
# Con firma digital (recomendado para publicar; es lo que quita el "editor desconocido"
# y lo que hace que el antivirus confie en el archivo):
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Firmar
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Firmar -Pfx firma\macdock.pfx -PfxPassword "clave"
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Firmar -Thumbprint 654BBBFEE00132B5...
#
# Cambiar la version (tiene que ser nueva en cada publicacion):
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Version 3.9.0 -Firmar
#
# Resultado:
#   dist\     (Instalar MacDock.exe, Desinstalar MacDock.exe, LEEME.txt, licencia de Inter)
#   descargas\MacDock-Instalador.zip  +  descargas\MacDock-Instalador.zip.sha256

param(
    [string]$Version,
    [switch]$Firmar,
    [string]$Thumbprint,
    [string]$Pfx,
    [string]$PfxPassword,
    [string]$SelloTiempo = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$build = Join-Path $root "build"
$dist = Join-Path $root "dist"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "No se encontro el compilador de C# de .NET Framework 4 ($csc)." }

New-Item -ItemType Directory -Force $build, $dist | Out-Null

# --- version: se toma de src\AssemblyInfo.cs si no se indica otra ---
$ai = Get-Content (Join-Path $src "AssemblyInfo.cs") -Raw
if (-not $Version) {
    if ($ai -match 'AssemblyFileVersion\("([\d\.]+)"\)') { $Version = $Matches[1] }
    else { throw "No se pudo leer la version de src\AssemblyInfo.cs." }
}
# se escribe una copia con la version pedida; el archivo original no se toca
$ai = $ai -replace 'AssemblyVersion\("[^"]*"\)', "AssemblyVersion(`"$Version`")"
$ai = $ai -replace 'AssemblyFileVersion\("[^"]*"\)', "AssemblyFileVersion(`"$Version`")"
$ai = $ai -replace 'AssemblyInformationalVersion\("[^"]*"\)', "AssemblyInformationalVersion(`"$Version`")"
$aiGen = Join-Path $build "AssemblyInfo.g.cs"
Set-Content -Path $aiGen -Value $ai -Encoding UTF8

# el manifiesto lleva la misma version
$man = Get-Content (Join-Path $src "app.manifest") -Raw
$man = $man -replace 'assemblyIdentity version="[^"]*"', "assemblyIdentity version=`"$Version`""
$manGen = Join-Path $build "app.g.manifest"
Set-Content -Path $manGen -Value $man -Encoding UTF8

$cs = "Native.cs", "Dock.cs", "Setup.cs", "Saver.cs", "Stack.cs", "MenuBar.cs", "Ui.cs", "FileOps.cs", "QuickLook.cs", "Spotlight.cs", "Finder.cs", "MacButtons.cs", "ShellMenu.cs", "AppIndex.cs", "Launchpad.cs", "AppSwitcher.cs", "Widgets.cs", "Wallpapers.cs", "ActivityMonitor.cs", "TrayApps.cs", "NotificationCenter.cs", "NowPlaying.cs", "Focus.cs", "MacWindow.cs", "MacChrome.cs", "MacButtonsPanel.cs", "FocusWindow.cs", "ClockApp.cs", "SysToggles.cs" | ForEach-Object { Join-Path $src $_ }
$cs += $aiGen
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
$args2 = @("/nologo", "/target:winexe", "/platform:x64", "/optimize+",
           "/win32icon:$src\dock.ico", "/win32manifest:$manGen") + $res +
         @("/out:$out", "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", "/r:System.Data.dll", "/r:System.Core.dll", "/r:Microsoft.CSharp.dll") + $cs
& $csc @args2
if ($LASTEXITCODE -ne 0) { throw "La compilacion fallo." }

# que no queden archivos viejos de una compilacion anterior dentro del zip
Remove-Item (Join-Path $dist "*") -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $out (Join-Path $dist "MacDock.exe") -Force
Copy-Item $out (Join-Path $dist "Instalar MacDock.exe") -Force
Copy-Item $out (Join-Path $dist "Desinstalar MacDock.exe") -Force
Copy-Item (Join-Path $src "LEEME.txt") $dist -Force
Copy-Item (Join-Path $src "fonts\OFL.txt") (Join-Path $dist "Inter-licencia-OFL.txt") -Force

# --- firma digital (opcional) ---
# El certificado se puede pasar por parametro o dejar en variables de entorno:
#   MACDOCK_CERT_THUMBPRINT, MACDOCK_PFX, MACDOCK_PFX_PASSWORD
$certificado = $null
if ($Firmar) {
    if (-not $Thumbprint) { $Thumbprint = $env:MACDOCK_CERT_THUMBPRINT }
    if (-not $Pfx) { $Pfx = $env:MACDOCK_PFX }
    if (-not $PfxPassword) { $PfxPassword = $env:MACDOCK_PFX_PASSWORD }

    if ($Thumbprint) {
        $certificado = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1
        if (-not $certificado) { throw "No hay ningun certificado con la huella $Thumbprint en el almacen." }
    }
    elseif ($Pfx) {
        $seguro = ConvertTo-SecureString $PfxPassword -AsPlainText -Force
        $certificado = Import-PfxCertificate -FilePath $Pfx -CertStoreLocation Cert:\CurrentUser\My -Password $seguro -Exportable
    }
    else {
        # el certificado de firma de codigo mas reciente que ya este instalado
        $certificado = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
    }
    if (-not $certificado) {
        throw "No encontre un certificado de firma de codigo instalado. Instala el certificado (o pasa -Pfx) y vuelve a correr el build."
    }
    Write-Host "Firmando con: $($certificado.Subject)" -ForegroundColor Cyan
    foreach ($f in Get-ChildItem $dist -Filter *.exe) {
        try {
            Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $certificado -TimestampServer $SelloTiempo -HashAlgorithm SHA256 -ErrorAction Stop | Out-Null
        }
        catch {
            Write-Warning "No se pudo sellar la hora con $SelloTiempo. Se firma sin sello de tiempo: $_"
            Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $certificado -HashAlgorithm SHA256 -ErrorAction Stop | Out-Null
        }
        Write-Host ("  {0}: {1}" -f $f.Name, (Get-AuthenticodeSignature $f.FullName).Status)
    }
}

$zipDir = Join-Path $root "descargas"; New-Item -ItemType Directory -Force $zipDir | Out-Null
$zip = Join-Path $zipDir "MacDock-Instalador.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $dist "*") -DestinationPath $zip -Force

# huella del zip: se publica junto al archivo para que cualquiera compruebe que no lo alteraron
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"$hash  MacDock-Instalador.zip" | Set-Content (Join-Path $zipDir "MacDock-Instalador.zip.sha256") -Encoding ASCII

$firma = Get-AuthenticodeSignature (Join-Path $dist "Instalar MacDock.exe")
Write-Host ""
Write-Host "Version:  $Version"
Write-Host "Firma:    $($firma.Status)"
if ($firma.SignerCertificate) { Write-Host "Firmante: $($firma.SignerCertificate.Subject)" }
Write-Host "SHA256:   $hash"
Write-Host "Listo:    $zip" -ForegroundColor Green
if ($firma.Status -eq "NotSigned") {
    Write-Host "Aviso: el instalador va SIN firmar. Windows lo va a marcar como editor desconocido y el antivirus lo trata con mas desconfianza. Corre build.ps1 -Firmar antes de publicarlo." -ForegroundColor Yellow
}
elseif ($firma.Status -ne "Valid") {
    Write-Host "Aviso: el archivo tiene firma, pero Windows no confia en ese certificado ($($firma.Status)). Si es un certificado de prueba, es lo esperado; para publicar hace falta uno comprado o de un programa de firma para codigo abierto." -ForegroundColor Yellow
}
