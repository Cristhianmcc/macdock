# Ajustes de Windows - bateria, apariencia y privacidad
# Todo sin administrador y reversible (opcion R). El respaldo se guarda en %LOCALAPPDATA%\MacDock\ajustes-respaldo.json

param([switch]$NoMenu, [switch]$Revertir)
$ErrorActionPreference = "Stop"
$Dir = Join-Path $env:LOCALAPPDATA "MacDock"
New-Item -ItemType Directory -Force $Dir | Out-Null
$BackupFile = Join-Path $Dir "ajustes-respaldo.json"
$script:NeedExplorerRestart = $false

Add-Type -Namespace AjW -Name Spi -MemberDefinition @'
[DllImport("user32.dll", EntryPoint="SystemParametersInfoW")] public static extern bool SpiRef(int a, int u, ref int p, int f);
[DllImport("user32.dll", EntryPoint="SystemParametersInfoW")] public static extern bool SpiArr(int a, int u, int[] p, int f);
[DllImport("user32.dll", EntryPoint="SystemParametersInfoW")] public static extern bool SpiPtr(int a, int u, IntPtr p, int f);
[DllImport("user32.dll", EntryPoint="SystemParametersInfoW", CharSet=CharSet.Unicode)] public static extern bool SpiStr(int a, int u, string p, int f);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, string l, int flags, int timeout, out IntPtr res);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
public static void Broadcast(string what) { IntPtr r; SendMessageTimeout((IntPtr)0xffff, 0x1A, IntPtr.Zero, what, 2, 2000, out r); }
'@

# ---------------- respaldo ----------------
function Load-Backup {
    if (Test-Path $BackupFile) {
        $j = Get-Content $BackupFile -Raw | ConvertFrom-Json
        if ($null -eq $j) { return @() }
        return @($j)
    }
    return @()
}
function Save-Backup($list) { ConvertTo-Json @($list) -Depth 5 | Set-Content $BackupFile -Encoding UTF8 }
function Has-Backup($id) { return [bool](Load-Backup | Where-Object { $_.id -eq $id }) }
function Add-Backup($entry) {
    if (Has-Backup $entry.id) { return }
    $list = @(Load-Backup); $list += $entry; Save-Backup $list
}

# ---------------- registro ----------------
function Get-RegValue($path, $name) {
    try { return (Get-ItemProperty -Path $path -Name $name -ErrorAction Stop).$name } catch { return $null }
}
function Set-RegValue($path, $name, $value, $type = "DWord") {
    $id = "reg|$path|$name"
    if (-not (Has-Backup $id)) {
        $old = Get-RegValue $path $name
        $kind = $null
        if ($old -ne $null) { try { $kind = (Get-Item $path).GetValueKind($name).ToString() } catch { $kind = $type } }
        $oldOut = $old; if ($old -is [byte[]]) { $oldOut = @($old | ForEach-Object { [int]$_ }) }
        Add-Backup ([pscustomobject]@{ id = $id; t = "reg"; path = $path; name = $name; existed = ($old -ne $null); value = $oldOut; kind = $kind })
    }
    if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
    New-ItemProperty -Path $path -Name $name -Value $value -PropertyType $type -Force | Out-Null
}

# ---------------- energia ----------------
$SUB_PROC = "54533251-82be-4824-96c1-47b60b740d00"; $PROC_MAX = "bc5038f7-23e0-4960-96da-33abaf5935ec"
$SUB_BTN  = "4f971e89-eebd-4455-a8de-9e59040e7347"; $LID      = "5ca83367-6e45-459f-a27b-476b1d01c936"
$SUB_ES   = "de830923-a562-41af-a086-e3a2c6bad2da"; $ES_THR   = "e69653ca-cf7f-4f05-aa73-cb833fa90ad4"
$SUB_VID  = "7516b95f-f776-4464-8c53-06167f40cc99"; $VID_OFF  = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"
$VID_DIM  = "17aaa29b-8b43-4b94-aafe-35f64daaf1ee"; $VID_ADAPT = "fbd9aa66-9553-4097-ba44-ed6e9d65eab8"
$SUB_SLP  = "238c9fa8-0aad-41ed-83f4-97be242c8f20"; $SLP_IDLE = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da"
function Get-PowerIndex($sub, $set, [bool]$dc) {
    $vals = @(powercfg /qh SCHEME_CURRENT $sub $set | ForEach-Object { if ($_ -match ':\s*0x([0-9a-fA-F]+)\s*$') { [Convert]::ToInt32($Matches[1], 16) } })
    if ($vals.Count -lt 2) { return $null }
    if ($dc) { return $vals[-1] } else { return $vals[-2] }
}
function Set-PowerIndex($sub, $set, [bool]$dc, [int]$value) {
    $id = "power|$sub|$set|$dc"
    if (-not (Has-Backup $id)) {
        Add-Backup ([pscustomobject]@{ id = $id; t = "power"; sub = $sub; set = $set; dc = $dc; value = (Get-PowerIndex $sub $set $dc) })
    }
    if ($dc) { powercfg /setdcvalueindex SCHEME_CURRENT $sub $set $value | Out-Null } else { powercfg /setacvalueindex SCHEME_CURRENT $sub $set $value | Out-Null }
    powercfg /setactive SCHEME_CURRENT | Out-Null
}

# ---------------- dock ----------------
$DockIni = Join-Path $Dir "config.ini"
$DockExe = Join-Path $Dir "MacDock.exe"
function Get-DockOpt($key) {
    if (-not (Test-Path $DockIni)) { return $null }
    $l = Get-Content $DockIni | Where-Object { $_ -match "^$key=" } | Select-Object -First 1
    if ($l) { return ($l -split '=', 2)[1].Trim() } return $null
}
function Set-DockOpt($key, $value) {
    $id = "dock|$key"
    if (-not (Has-Backup $id)) { Add-Backup ([pscustomobject]@{ id = $id; t = "dock"; key = $key; value = (Get-DockOpt $key) }) }
    $lines = @(); if (Test-Path $DockIni) { $lines = @(Get-Content $DockIni | Where-Object { $_ -notmatch "^$key=" }) }
    $lines += "$key=$value"; Set-Content $DockIni $lines
    Restart-Dock
}
function Restart-Dock {
    if (-not (Test-Path $DockExe)) { return }
    Start-Process $DockExe -ArgumentList "--exit"
    for ($i = 0; $i -lt 30; $i++) { if (-not (Get-Process MacDock -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 100 }
    Get-Process MacDock -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Process $DockExe -WorkingDirectory $Dir
}

# ---------------- animaciones (SystemParametersInfo) ----------------
function Get-Anim {
    $ca = 0; [AjW.Spi]::SpiRef(0x1042, 0, [ref]$ca, 0) | Out-Null
    $ai = [int[]](8, 0); [AjW.Spi]::SpiArr(0x48, 8, $ai, 0) | Out-Null
    return @{ client = $ca; minmax = $ai[1] }
}
function Set-Anim([int]$client, [int]$minmax) {
    [AjW.Spi]::SpiPtr(0x1043, 0, [IntPtr]$client, 3) | Out-Null
    $ai = [int[]](8, $minmax); [AjW.Spi]::SpiArr(0x49, 8, $ai, 3) | Out-Null
}

# ---------------- definicion de ajustes ----------------
$adv = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
$per = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
$cdm = "HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
$ctxKey = "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}"

function Status($n) {
    switch ($n) {
        1 { return (Get-DockOpt "AutoRefresh") -eq "1" }
        2 { return (Get-PowerIndex $SUB_PROC $PROC_MAX $true) -eq 99 }
        3 { return (Get-PowerIndex $SUB_BTN $LID $true) -eq 2 }
        6 { return Test-Path "$ctxKey\InprocServer32" }
        7 { return (Get-RegValue $adv "HideFileExt") -eq 0 }
        8 { return (Get-RegValue "HKCU:\Software\Microsoft\Clipboard" "EnableClipboardHistory") -eq 1 }
        9 { return (Get-Anim).client -eq 0 }
        10 { return (Get-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo" "Enabled") -eq 0 }
        11 { return (Get-DockOpt "AppNap") -eq "1" }
        12 { return (Get-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications" "GlobalUserDisabled") -eq 1 }
        13 { return (Get-RegValue "HKCU:\System\GameConfigStore" "GameDVR_Enabled") -eq 0 }
        14 { return (Get-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" "StartupDelayInMSec") -eq 0 }
        15 { return (Get-RegValue "HKCU:\Control Panel\Desktop" "MenuShowDelay") -eq "100" }
        16 { return (Get-DockOpt "HotCorners") -eq "1" }
        17 { return ("{0:X}" -f (Get-RegValue "HKCU:\Software\Microsoft\Windows\DWM" "AccentColor")) -eq "FFFF7A00" }
        18 { return (Get-RegValue "HKCU:\Control Panel\Cursors" "(default)") -eq "MacDock" }
        19 { return (Get-RegValue "HKCU:\AppEvents\Schemes" "(default)") -eq ".None" }
        20 { return (Get-RegValue "HKCU:\Control Panel\Desktop" "WallPaper") -eq $MacWall }
        21 { return (Get-DockOpt "MenuBar") -eq "1" }
        22 { return (Get-RegValue "HKCU:\Control Panel\Desktop" "FontSmoothingType") -eq 1 }
        23 { return (Has-Backup "lock") -and (Get-RegValue $cdm "RotatingLockScreenEnabled") -eq 0 }
        24 { return (Get-RegValue "HKCU:\Control Panel\Cursors" "CursorBaseSize") -eq 40 }
        25 { return (Get-RegValue $adv "Start_Layout") -eq 1 }
        26 { return (Get-RegValue $adv "LaunchTo") -eq 1 -and (Has-Backup "reg|HKCU:\Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}|System.IsPinnedToNameSpaceTree") }
        27 { return (Get-DockOpt "AutoTheme") -eq "1" }
        28 { return (Get-DockOpt "DynWall") -eq "1" }
        29 { return (Get-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings" "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND") -eq 0 }
        30 { return (Get-PowerIndex $SUB_ES $ES_THR $true) -eq 40 }
        31 { return (Get-PowerIndex $SUB_VID $VID_OFF $true) -eq 180 }
        32 { return (Has-Backup "bags") }
        33 { return [bool](Get-RegValue "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons" "3") }
    }
    return $null
}

function Mark($n) { $s = Status $n; if ($s -eq $true) { return "[activo]" } elseif ($s -eq $false) { return "        " } else { return "        " } }

function Apply($n) {
    switch ($n) {
        1 {
            if (-not (Test-Path $DockExe)) { Write-Host "  Necesita el dock (MacDock) instalado." -ForegroundColor Yellow; return }
            Set-DockOpt "AutoRefresh" "1"
            Write-Host "  Listo: con bateria la pantalla baja a 60 Hz y enchufado vuelve a la maxima." -ForegroundColor Green
        }
        2 {
            Set-PowerIndex $SUB_PROC $PROC_MAX $true 99
            Write-Host "  Listo: con bateria el procesador no usa Turbo (menos calor y mas bateria). Enchufado sigue igual." -ForegroundColor Green
        }
        3 {
            Set-PowerIndex $SUB_BTN $LID $true 2
            Write-Host "  Listo: con bateria, al cerrar la tapa hiberna (gasto cero). Enchufado sigue suspendiendo." -ForegroundColor Green
        }
        11 {
            if (-not (Test-Path $DockExe)) { Write-Host "  Necesita el dock (MacDock) instalado." -ForegroundColor Yellow; return }
            Set-DockOpt "AppNap" "1"
            Write-Host "  Listo: con bateria, las apps que no usas pasan a modo eficiencia (las que suenan, no)." -ForegroundColor Green
        }
        12 {
            Set-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications" "GlobalUserDisabled" 1
            Set-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Search" "BackgroundAppGlobalToggle" 0
            Write-Host "  Listo: las apps de la tienda ya no corren escondidas en segundo plano." -ForegroundColor Green
        }
        13 {
            Set-RegValue "HKCU:\System\GameConfigStore" "GameDVR_Enabled" 0
            Set-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR" "AppCaptureEnabled" 0
            Write-Host "  Listo: Xbox Game Bar ya no graba en segundo plano." -ForegroundColor Green
        }
        14 {
            Set-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" "StartupDelayInMSec" 0
            Set-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" "WaitForIdleState" 0
            Write-Host "  Listo: los programas de inicio arrancan sin la espera que pone Windows." -ForegroundColor Green
        }
        15 {
            Set-RegValue "HKCU:\Control Panel\Desktop" "MenuShowDelay" "100" "String"
            [AjW.Spi]::SpiPtr(0x006B, 100, [IntPtr]::Zero, 3) | Out-Null
            Write-Host "  Listo: los submenus se abren al instante." -ForegroundColor Green
        }
        16 {
            if (-not (Test-Path $DockExe)) { Write-Host "  Necesita el dock (MacDock) instalado." -ForegroundColor Yellow; return }
            Set-DockOpt "HotCorners" "1"
            Write-Host "  Listo: deja el mouse en la esquina arriba-izquierda = todas las ventanas; abajo-izquierda = escritorio." -ForegroundColor Green
        }
        17 { Apply-MacAccent; Write-Host "  Listo: color de acento azul de Mac en botones, enlaces, selecciones e Inicio." -ForegroundColor Green }
        18 { if (Apply-MacCursor) { Write-Host "  Listo: cursor estilo Mac, nitido en cualquier tamano." -ForegroundColor Green } }
        19 { Apply-Silence; Write-Host "  Listo: Windows ya no hace sonidos (las apps y la musica suenan normal)." -ForegroundColor Green }
        20 { Apply-MacWallpaper; Write-Host "  Listo: fondo de pantalla estilo Mac (tu fondo anterior vuelve con R)." -ForegroundColor Green }
        21 {
            if (-not (Test-Path $DockExe)) { Write-Host "  Necesita el dock (MacDock) instalado." -ForegroundColor Yellow; return }
            Set-DockOpt "MenuBar" "1"
            Write-Host "  Listo: barra superior con menu del sistema, app activa, Wi-Fi, volumen, bateria y hora." -ForegroundColor Green
        }
        22 { Apply-SoftText; Write-Host "  Listo: letras suavizadas en gris, como en Mac (algunas apps al reabrirlas)." -ForegroundColor Green }
        23 { Apply-LockScreen; Write-Host "  Listo: la pantalla de bloqueo usa la aurora, sin fotos ni anuncios de Microsoft." -ForegroundColor Green }
        24 { Apply-BigCursor; Write-Host "  Listo: cursor un poco mas grande." -ForegroundColor Green }
        25 { Apply-CleanStart; Write-Host "  Listo: menu Inicio con mas espacio para tus apps y sin 'Recomendado' (al reiniciar el Explorador)." -ForegroundColor Green }
        26 { Apply-Finder; Write-Host "  Listo: Explorador con tus favoritos en la barra lateral, sin Galeria, abre en 'Este equipo'." -ForegroundColor Green }
        27 {
            if (-not (Test-Path $DockExe)) { Write-Host "  Necesita el dock (MacDock) instalado." -ForegroundColor Yellow; return }
            Set-RegValue $per "AppsUseLightTheme" ([int](Get-RegValue $per "AppsUseLightTheme"))
            Set-RegValue $per "SystemUsesLightTheme" ([int](Get-RegValue $per "SystemUsesLightTheme"))
            Set-DockOpt "AutoTheme" "1"
            Write-Host "  Listo: modo claro de 7:00 a 18:30 y oscuro el resto (cambia las horas en config.ini)." -ForegroundColor Green
        }
        28 {
            if (-not (Test-Path $DockExe)) { Write-Host "  Necesita el dock (MacDock) instalado." -ForegroundColor Yellow; return }
            New-DynamicWallpapers
            Set-RegValue "HKCU:\Control Panel\Desktop" "WallPaper" ([string](Get-RegValue "HKCU:\Control Panel\Desktop" "WallPaper")) "String"
            Set-DockOpt "DynWall" "1"
            Write-Host "  Listo: el fondo cambia: amanecer (6-9), dia (9-17), atardecer (17-19:30) y noche." -ForegroundColor Green
        }
        29 {
            $ns = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings"
            Set-RegValue $ns "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND" 0
            Set-RegValue $ns "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK" 0
            Set-RegValue "$ns\Windows.SystemToast.Suggested" "Enabled" 0
            Set-RegValue "$ns\Windows.SystemToast.HelloFace" "Enabled" 0
            Set-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement" "ScoobeSystemSettingEnabled" 0
            Write-Host "  Listo: notificaciones sin sonido, sin sugerencias de Windows y no aparecen en la pantalla de bloqueo." -ForegroundColor Green
            Write-Host "  Para 'No molestar' de noche: se abrio Configuracion > Notificaciones > 'Activar No molestar automaticamente'." -ForegroundColor DarkGray
            if (-not $script:Silent) { Start-Process "ms-settings:notifications" }
        }
        30 {
            Set-PowerIndex $SUB_ES $ES_THR $true 40
            Write-Host "  Listo: el ahorro de energia se activa solo al 40% (antes al 30%)." -ForegroundColor Green
        }
        31 {
            Set-PowerIndex $SUB_VID $VID_DIM $true 60       # atenua al minuto
            Set-PowerIndex $SUB_VID $VID_OFF $true 180      # apaga la pantalla a los 3 min
            Set-PowerIndex $SUB_SLP $SLP_IDLE $true 900     # suspende a los 15 min
            Set-PowerIndex $SUB_VID $VID_ADAPT $true 1      # brillo adaptable (si la laptop tiene sensor)
            Write-Host "  Listo: con bateria la pantalla se atenua al minuto, se apaga a los 3 y la laptop duerme a los 15 (antes 2 horas)." -ForegroundColor Green
        }
        32 { Apply-FinderLook; Write-Host "  Listo: el Explorador abre las carpetas con iconos grandes, sin grupos, y sin 'Inicio' en la barra lateral." -ForegroundColor Green }
        33 { if (Apply-BlueFolders) { Write-Host "  Listo: carpetas azules estilo Mac (se ven al reiniciar el Explorador)." -ForegroundColor Green } }
        4 { Apply-Gpu }
        5 { Apply-Startup }
        6 {
            if (-not (Test-Path $ctxKey)) { Add-Backup ([pscustomobject]@{ id = "regkey|$ctxKey"; t = "regkey"; path = $ctxKey; existed = $false }) }
            New-Item -Path "$ctxKey\InprocServer32" -Force | Out-Null
            Set-ItemProperty -Path "$ctxKey\InprocServer32" -Name "(default)" -Value ""
            $script:NeedExplorerRestart = $true
            Write-Host "  Listo: menu de clic derecho completo (se ve al reiniciar el Explorador)." -ForegroundColor Green
            Write-Host "  Nota: algunas versiones nuevas de Windows 11 lo ignoran. Atajo que siempre funciona: Shift + clic derecho." -ForegroundColor DarkGray
        }
        7 {
            Set-RegValue $adv "HideFileExt" 0
            $script:NeedExplorerRestart = $true
            Write-Host "  Listo: el Explorador muestra extensiones (.ps1, .py, .mp4...)." -ForegroundColor Green
        }
        8 {
            Set-RegValue "HKCU:\Software\Microsoft\Clipboard" "EnableClipboardHistory" 1
            Start-Process "ms-settings:clipboard"
            Write-Host "  Se abrio Configuracion > Portapapeles: activa 'Historial del Portapapeles' (Windows pide ese clic)." -ForegroundColor Green
            Write-Host "  Luego usa Windows+V (no Ctrl+V) para elegir entre lo que copiaste." -ForegroundColor Green
        }
        9 {
            if (-not (Has-Backup "anim")) { $a = Get-Anim; Add-Backup ([pscustomobject]@{ id = "anim"; t = "anim"; client = $a.client; minmax = $a.minmax }) }
            Set-Anim 0 0
            Write-Host "  Listo: sin animaciones, todo responde al instante." -ForegroundColor Green
        }
        10 {
            Set-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo" "Enabled" 0
            Set-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy" "TailoredExperiencesWithDiagnosticDataEnabled" 0
            Set-RegValue $cdm "SilentInstalledAppsEnabled" 0
            Set-RegValue $cdm "SoftLandingEnabled" 0
            Set-RegValue $cdm "SubscribedContent-310093Enabled" 0
            Set-RegValue $cdm "SubscribedContent-338393Enabled" 0
            Set-RegValue $cdm "SubscribedContent-353694Enabled" 0
            Set-RegValue $cdm "SubscribedContent-353696Enabled" 0
            Set-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement" "ScoobeSystemSettingEnabled" 0
            Write-Host "  Listo: sin ID de publicidad, sin sugerencias ni apps instaladas en silencio." -ForegroundColor Green
        }
    }
}

# ---------------- apariencia Mac ----------------
$CursorNames = "Arrow,Help,AppStarting,Wait,Crosshair,IBeam,NWPen,No,SizeNS,SizeWE,SizeNWSE,SizeNESW,SizeAll,UpArrow,Hand,Pin,Person" -split ','
$PicDir = Join-Path ([Environment]::GetFolderPath("MyPictures")) "MacDock"   # Windows solo aplica fondos desde carpetas normales como Imagenes
New-Item -ItemType Directory -Force $PicDir | Out-Null
$MacWall = Join-Path $PicDir "fondo-mac.png"

function Refresh-Look {
    $fst = Get-RegValue "HKCU:\Control Panel\Desktop" "FontSmoothingType"
    if ($fst) { [AjW.Spi]::SpiPtr(0x200B, 0, [IntPtr][int]$fst, 3) | Out-Null }    # suavizado de texto
    [AjW.Spi]::SpiPtr(0x57, 0, [IntPtr]::Zero, 3) | Out-Null                       # recargar cursores
    $wp = Get-RegValue "HKCU:\Control Panel\Desktop" "WallPaper"
    if ($wp) { [AjW.Spi]::SpiStr(0x14, 0, $wp, 3) | Out-Null }                     # fondo
    [AjW.Spi]::Broadcast("ImmersiveColorSet")                                      # color de acento
}

function Apply-MacAccent {
    # Azul de macOS (#007AFF). En el registro va como ABGR.
    $dwm = "HKCU:\Software\Microsoft\Windows\DWM"; $acc = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"
    Set-RegValue "HKCU:\Control Panel\Desktop" "AutoColorization" 0
    Set-RegValue $dwm "AccentColor" 0xFFFF7A00 "DWord"      # en PowerShell este literal ya es el DWORD correcto
    Set-RegValue $dwm "ColorPrevalence" 0
    Set-RegValue $acc "AccentColorMenu" 0xFFFF7A00 "DWord"
    Set-RegValue $acc "StartColorMenu" 0xFFCC6200 "DWord"
    $pal = [byte[]](0x99,0xCC,0xFF,0, 0x66,0xB2,0xFF,0, 0x33,0x95,0xFF,0, 0x00,0x7A,0xFF,0, 0x00,0x62,0xCC,0, 0x00,0x49,0x99,0, 0x00,0x31,0x66,0, 0x88,0x88,0x88,0)
    Set-RegValue $acc "AccentPalette" $pal "Binary"
    Refresh-Look
}

# Genera un cursor estilo Mac (flecha negra con borde blanco y sombra suave), original, en varios tamanos nitidos.
# Uso: . cursor-mac.ps1 ; New-MacArrowCursor "C:\ruta\mac-flecha.cur"
function New-MacArrowCursor([string]$Out) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 32, 40, 48, 56, 64, 80, 96, 128
    # forma de la flecha (alto = 1)
    $shape = @(@(0, 0), @(0, 0.80), @(0.19, 0.63), @(0.32, 0.93), @(0.43, 0.88), @(0.30, 0.59), @(0.55, 0.59))
    $frames = @()
    foreach ($S in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "HighQuality"; $g.Clear([System.Drawing.Color]::Transparent)
        $H = $S * 0.66; $m = [Math]::Max(1.5, $S * 0.06)
        $pts = [System.Drawing.PointF[]]($shape | ForEach-Object { New-Object System.Drawing.PointF ([float]($m + $_[0] * $H)), ([float]($m + $_[1] * $H)) })
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath; $path.AddPolygon($pts)
        # sombra suave
        $sh = $path.Clone(); $mx = New-Object System.Drawing.Drawing2D.Matrix; $mx.Translate([float]($S * 0.025), [float]($S * 0.04)); $sh.Transform($mx)
        foreach ($k in 3, 2, 1) {
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(22, 0, 0, 0)), ([float]($H * 0.06 * $k)); $pen.LineJoin = "Round"
            $g.DrawPath($pen, $sh); $pen.Dispose()
        }
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 0, 0, 0))), $sh)
        # borde blanco y relleno negro
        $wpen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([float]([Math]::Max(1.6, $H * 0.085))); $wpen.LineJoin = "Round"
        $g.DrawPath($wpen, $path)
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 18, 20))), $path)
        $g.Dispose()
        # entrada DIB de 32 bits (como los cursores de Windows) + mascara vacia
        $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$S); $bw.Write([int32]($S * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]0); $bw.Write([uint32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $S, $S), "ReadOnly", "Format32bppArgb")
        $row = New-Object byte[] ($S * 4)
        for ($y = $S - 1; $y -ge 0; $y--) { [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length); $bw.Write($row) }
        $bmp.UnlockBits($data); $bmp.Dispose()
        $maskRow = [int]([Math]::Ceiling($S / 32.0) * 4); $bw.Write((New-Object byte[] ($maskRow * $S)))
        $bw.Flush()
        $hot = [int][Math]::Round($m)
        $frames += , @($S, $ms.ToArray(), $hot)
    }
    $curMs = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $curMs
    $w.Write([uint16]0); $w.Write([uint16]2); $w.Write([uint16]$frames.Count)   # tipo 2 = cursor
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) { $s2 = $f[0]; $w.Write([byte]$s2); $w.Write([byte]$s2); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]$f[2]); $w.Write([uint16]$f[2]); $w.Write([uint32]$f[1].Length); $w.Write([uint32]$offset); $offset += $f[1].Length }
    foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
    $w.Flush()
    New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
    [IO.File]::WriteAllBytes($Out, $curMs.ToArray())
}

function Apply-MacCursor {
    # flecha estilo Mac dibujada por codigo (nitida en todos los tamanos) + cursores Aero de Windows para el resto
    $file = Join-Path $PicDir "mac-flecha.cur"
    New-MacArrowCursor $file
    $aero = (Get-RegValue "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes" "Windows Aero") -split ','
    $cur = "HKCU:\Control Panel\Cursors"
    for ($i = 0; $i -lt $CursorNames.Count -and $i -lt $aero.Count; $i++) { if ($aero[$i].Trim()) { Set-RegValue $cur $CursorNames[$i] $aero[$i].Trim() "ExpandString" } }
    Set-RegValue $cur "Arrow" $file "ExpandString"
    Set-RegValue $cur "(default)" "MacDock" "String"
    Refresh-Look
    return $true
}
function Apply-Silence {
    Set-RegValue "HKCU:\AppEvents\Schemes" "(default)" ".None" "String"
    Get-ChildItem "HKCU:\AppEvents\Schemes\Apps" -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem $_.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
            $k = Join-Path $_.PSPath ".Current"
            if (Test-Path $k) {
                $v = Get-RegValue $k "(default)"
                if ($v) { Set-RegValue ($k -replace '^Microsoft\.PowerShell\.Core\\Registry::HKEY_CURRENT_USER', 'HKCU:') "(default)" "" "String" }
            }
        }
    }
}

function New-MacWallpaper([string]$Out = $MacWall, [string]$Variant = "noche") {
    Add-Type -AssemblyName System.Drawing
    [AjW.Spi]::SetProcessDPIAware() | Out-Null
    $W = [AjW.Spi]::GetSystemMetrics(0); $H = [AjW.Spi]::GetSystemMetrics(1)
    if ($W -lt 800) { $W = 1920; $H = 1080 }
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.CompositingQuality = "HighQuality"
    $rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
    # paletas por momento del dia: fondo (2 colores) + manchas de luz (x, y, ancho, alto, R, G, B, opacidad)
    switch ($Variant) {
        "amanecer" {
            $c1 = @(44, 46, 104); $c2 = @(246, 160, 132)
            $blobs = @(@(0.20, 0.85, 0.80, 0.60, 255, 170, 120, 200), @(0.60, 0.95, 0.85, 0.50, 255, 120, 140, 170),
                       @(0.85, 0.55, 0.60, 0.55, 255, 196, 150, 160), @(0.30, 0.25, 0.70, 0.50, 120, 130, 230, 150),
                       @(0.80, 0.15, 0.55, 0.45, 190, 140, 240, 130), @(0.05, 0.35, 0.45, 0.40, 255, 150, 190, 110)) }
        "dia" {
            $c1 = @(118, 170, 255); $c2 = @(196, 168, 255)
            $blobs = @(@(0.20, 0.80, 0.80, 0.60, 255, 255, 255, 150), @(0.60, 0.95, 0.85, 0.50, 255, 196, 230, 170),
                       @(0.85, 0.60, 0.60, 0.55, 160, 230, 255, 170), @(0.30, 0.25, 0.70, 0.50, 90, 150, 255, 140),
                       @(0.80, 0.15, 0.55, 0.45, 210, 180, 255, 150), @(0.05, 0.35, 0.45, 0.40, 140, 220, 255, 120)) }
        "atardecer" {
            $c1 = @(56, 20, 74); $c2 = @(236, 108, 64)
            $blobs = @(@(0.20, 0.82, 0.80, 0.60, 255, 140, 50, 200), @(0.60, 0.95, 0.85, 0.50, 255, 90, 90, 180),
                       @(0.85, 0.60, 0.60, 0.55, 240, 70, 150, 170), @(0.30, 0.25, 0.70, 0.50, 120, 60, 200, 150),
                       @(0.80, 0.15, 0.55, 0.45, 255, 120, 80, 130), @(0.05, 0.35, 0.45, 0.40, 255, 190, 90, 110)) }
        default {
            $c1 = @(12, 18, 52); $c2 = @(58, 24, 92)
            $blobs = @(@(0.18, 0.78, 0.75, 0.55, 255, 94, 58, 180), @(0.55, 0.95, 0.85, 0.50, 255, 140, 60, 170),
                       @(0.85, 0.65, 0.65, 0.60, 236, 64, 160, 170), @(0.35, 0.30, 0.70, 0.50, 72, 110, 255, 150),
                       @(0.80, 0.20, 0.55, 0.45, 150, 80, 255, 140), @(0.05, 0.25, 0.45, 0.40, 40, 160, 255, 110)) }
    }
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, $c1[0], $c1[1], $c1[2])), ([System.Drawing.Color]::FromArgb(255, $c2[0], $c2[1], $c2[2])), 70
    $g.FillRectangle($bg, $rect)
    # manchas de luz suaves (aurora), estilo de los fondos de macOS pero original
    foreach ($b in $blobs) {
        $cx = $b[0] * $W; $cy = $b[1] * $H; $rw = $b[2] * $W; $rh = $b[3] * $H
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddEllipse([float]($cx - $rw / 2), [float]($cy - $rh / 2), [float]$rw, [float]$rh)
        $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
        $pgb.CenterColor = [System.Drawing.Color]::FromArgb([int]$b[7], [int]$b[4], [int]$b[5], [int]$b[6])
        $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, [int]$b[4], [int]$b[5], [int]$b[6]))
        $g.FillPath($pgb, $path); $pgb.Dispose(); $path.Dispose()
    }
    $g.Dispose(); $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

function Apply-MacWallpaper {
    New-MacWallpaper
    Set-RegValue "HKCU:\Control Panel\Desktop" "WallPaper" $MacWall "String"
    Set-RegValue "HKCU:\Control Panel\Desktop" "WallpaperStyle" "10" "String"
    Refresh-Look
}

# 22: texto suavizado en gris, como Mac (en vez de ClearType)
function Apply-SoftText {
    Set-RegValue "HKCU:\Control Panel\Desktop" "FontSmoothing" "2" "String"
    Set-RegValue "HKCU:\Control Panel\Desktop" "FontSmoothingType" 1
    Refresh-Look
}

# 23: pantalla de bloqueo con la aurora (sin administrador, API oficial de Windows)
$LockOrig = Join-Path $Dir "bloqueo-original.jpg"
function Invoke-WinRT {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime] | Out-Null
    [Windows.System.UserProfile.LockScreen, Windows.System.UserProfile, ContentType = WindowsRuntime] | Out-Null
    $script:AsTaskOp = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
    $script:AsTaskAct = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1
}
function Set-LockImage([string]$path) {
    Invoke-WinRT
    $t = $script:AsTaskOp.MakeGenericMethod([Windows.Storage.StorageFile]).Invoke($null, @([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)))
    $t.Wait(-1) | Out-Null
    $a = $script:AsTaskAct.Invoke($null, @([Windows.System.UserProfile.LockScreen]::SetImageFileAsync($t.Result)))
    $a.Wait(-1) | Out-Null
}
function Apply-LockScreen {
    Invoke-WinRT
    if (-not (Test-Path $LockOrig)) {
        try {
            $st = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead([Windows.System.UserProfile.LockScreen]::GetImageStream())
            $fs = [IO.File]::Create($LockOrig); $st.CopyTo($fs); $fs.Close(); $st.Close()
        } catch { }
    }
    Add-Backup ([pscustomobject]@{ id = "lock"; t = "lock"; orig = $LockOrig })
    Set-RegValue $cdm "RotatingLockScreenEnabled" 0
    Set-RegValue $cdm "RotatingLockScreenOverlayEnabled" 0
    if (-not (Test-Path $MacWall)) { New-MacWallpaper }
    Set-LockImage $MacWall
}

# 24: cursor un poco mas grande (Mac lo tiene mas visible)
function Apply-BigCursor {
    Set-RegValue "HKCU:\Control Panel\Cursors" "CursorBaseSize" 40
    Refresh-Look
}

# 25: menu Inicio limpio
function Apply-CleanStart {
    Set-RegValue $adv "Start_Layout" 1                    # mas espacio para apps fijadas
    Set-RegValue $adv "Start_TrackDocs" 0                 # sin archivos recientes
    Set-RegValue $adv "Start_TrackProgs" 0                # sin "mas usadas"
    Set-RegValue $adv "Start_IrisRecommendations" 0       # sin consejos
    Set-RegValue $adv "Start_AccountNotifications" 0      # sin avisos de cuenta
    try { Set-RegValue "HKCU:\Software\Policies\Microsoft\Windows\Explorer" "HideRecommendedSection" 1 } catch { }   # en algunas ediciones Windows no lo permite sin administrador
    $script:NeedExplorerRestart = $true
}

# 26: Explorador tipo Finder: favoritos en la barra lateral, sin Galeria, abre en "Este equipo"
function Apply-Finder {
    Set-RegValue $adv "LaunchTo" 1
    Set-RegValue "HKCU:\Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}" "System.IsPinnedToNameSpaceTree" 0
    $sh = New-Object -ComObject Shell.Application
    $pinned = @($sh.Namespace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}").Items() | ForEach-Object { $_.Path })
    $want = @([Environment]::GetFolderPath("Desktop"), (Join-Path $env:USERPROFILE "Downloads"), [Environment]::GetFolderPath("MyDocuments"))
    $desk = [Environment]::GetFolderPath("Desktop")
    $folders = @(Get-ChildItem $desk -Directory -ErrorAction SilentlyContinue | Where-Object { -not $_.Attributes.HasFlag([IO.FileAttributes]::Hidden) } | Sort-Object LastWriteTime -Descending)
    if ($Host.UI.RawUI -and -not $script:Silent -and $folders.Count -gt 0) {
        Write-Host ""
        Write-Host "  Tus carpetas del Escritorio (las mas usadas primero). Cuales fijo en la barra lateral?" -ForegroundColor Cyan
        $show = $folders | Select-Object -First 15
        for ($i = 0; $i -lt $show.Count; $i++) { Write-Host ("  {0,2}. {1}" -f ($i + 1), $show[$i].Name) }
        $ans = Read-Host "  Numeros separados por coma (Enter = ninguna)"
        if ($ans) { $ans -split ',' | ForEach-Object { $n = [int]$_.Trim() - 1; if ($n -ge 0 -and $n -lt $show.Count) { $want += $show[$n].FullName } } }
    } elseif ($script:ExtraPins) { $want += $script:ExtraPins }
    foreach ($p in $want) {
        if (-not (Test-Path $p)) { continue }
        if ($pinned -contains $p) { continue }
        $item = $sh.Namespace((Split-Path $p -Parent)).ParseName((Split-Path $p -Leaf))
        if (-not $item) { $item = $sh.Namespace($p).Self }
        $item.InvokeVerb("pintohome")
        Add-Backup ([pscustomobject]@{ id = "pin|$p"; t = "pin"; path = $p })
    }
    $script:NeedExplorerRestart = $true
}

function Unpin-Home([string]$p) {
    $sh = New-Object -ComObject Shell.Application
    $qa = $sh.Namespace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}")
    foreach ($it in $qa.Items()) { if ($it.Path -eq $p) { $it.InvokeVerb("unpinfromhome") } }
}

# 27 y 28 los hace el dock (ya esta corriendo, no suma procesos)
function New-DynamicWallpapers {
    $fd = $PicDir
    foreach ($v in "amanecer", "dia", "atardecer", "noche") { New-MacWallpaper (Join-Path $fd "$v.png") $v }
}

# 32: Explorador mas parecido a Finder: iconos grandes en todas las carpetas, sin agrupar, sin "Inicio" en la barra lateral
$FolderTypes = @("{5C4F28B5-F869-4E84-8E60-F11DB97C5CC7}", "{7D49D726-3C21-4F05-99AA-FDC2C9474656}", "{B3690E58-E961-423B-B687-386EBFD83239}",
                 "{885A186E-A440-4ADA-A3BA-D2D02E54E5E3}", "{94D6DDCC-4A68-4175-A374-BD584A510B78}", "{5FA96407-7E77-483C-AC93-691D05850DE8}")
$BagsKey = "HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell"
function Apply-FinderLook {
    $bak = Join-Path $Dir "vistas-carpetas-respaldo.reg"
    if (-not (Test-Path $bak)) { reg.exe export $BagsKey $bak /y 2>$null | Out-Null }
    Add-Backup ([pscustomobject]@{ id = "bags"; t = "bags"; file = $bak })
    reg.exe delete "$BagsKey\Bags" /f 2>$null | Out-Null
    reg.exe delete "$BagsKey\BagMRU" /f 2>$null | Out-Null
    foreach ($ft in $FolderTypes) {
        $k = "HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell\$ft"
        New-Item -Path $k -Force | Out-Null
        Set-ItemProperty $k "FolderType" $ft
        Set-ItemProperty $k "Mode" 1 -Type DWord               # iconos
        Set-ItemProperty $k "LogicalViewMode" 3 -Type DWord    # iconos
        Set-ItemProperty $k "IconSize" 96 -Type DWord          # iconos grandes
        Set-ItemProperty $k "GroupView" 0 -Type DWord          # sin grupos (Descargas ya no se agrupa por fecha)
        Set-ItemProperty $k "GroupByKey:PID" 0 -Type DWord
        Set-ItemProperty $k "GroupByDirection" 1 -Type DWord
    }
    Set-RegValue $adv "UseCompactMode" 0
    Set-RegValue "HKCU:\Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}" "System.IsPinnedToNameSpaceTree" 0   # quitar "Inicio"
    $script:NeedExplorerRestart = $true
}

# 33: iconos de carpeta azules estilo Mac (dibujados por codigo, no son de Apple). Pide administrador una vez.
$PubDir = Join-Path $env:PUBLIC "MacDock"
$FolderIco = Join-Path $PubDir "carpeta-azul.ico"
function New-BlueFolderIcon {
    Add-Type -AssemblyName System.Drawing
    New-Item -ItemType Directory -Force $PubDir | Out-Null
    $frames = @()
    foreach ($sz in 256, 128, 64, 48, 32, 24, 16) {
        $bmp = New-Object System.Drawing.Bitmap $sz, $sz
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "HighQuality"
        $u = $sz / 256.0
        function RR($x, $y, $w, $h, $r) { $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = $r * 2; $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90); $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure(); $p }
        # parte trasera con pestana
        $back = New-Object System.Drawing.Drawing2D.GraphicsPath; $back.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding
        $back.AddPath((RR (16*$u) (44*$u) (224*$u) (172*$u) (16*$u)), $false)
        $back.AddPath((RR (16*$u) (30*$u) (92*$u) (40*$u) (12*$u)), $false)
        $bb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, (30*$u), $sz, (190*$u)), ([System.Drawing.Color]::FromArgb(255, 74, 150, 226)), ([System.Drawing.Color]::FromArgb(255, 52, 122, 204)), 90
        $g.FillPath($bb, $back)
        # frente
        $front = RR (16*$u) (74*$u) (224*$u) (150*$u) (16*$u)
        $fb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, (74*$u), $sz, (150*$u)), ([System.Drawing.Color]::FromArgb(255, 132, 196, 252)), ([System.Drawing.Color]::FromArgb(255, 86, 160, 240)), 90
        $g.FillPath($fb, $front)
        $hl = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 255, 255, 255)), ([float][Math]::Max(1, 3*$u))
        $g.DrawLine($hl, [float](30*$u), [float](76*$u), [float](226*$u), [float](76*$u))
        $g.Dispose()
        if ($sz -ge 256) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $data = $ms.ToArray() }
        else {
            $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $ms
            $bw.Write([uint32]40); $bw.Write([int32]$sz); $bw.Write([int32]($sz * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
            $bw.Write([uint32]0); $bw.Write([uint32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
            for ($yy = $sz - 1; $yy -ge 0; $yy--) { for ($xx = 0; $xx -lt $sz; $xx++) { $px = $bmp.GetPixel($xx, $yy); $bw.Write([byte]$px.B); $bw.Write([byte]$px.G); $bw.Write([byte]$px.R); $bw.Write([byte]$px.A) } }
            $maskRow = [int]([Math]::Ceiling($sz / 32.0) * 4); $bw.Write((New-Object byte[] ($maskRow * $sz)))
            $bw.Flush(); $data = $ms.ToArray()
        }
        $bmp.Dispose()
        $frames += ,@($sz, $data)
    }
    $out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($fr in $frames) { $s2 = $fr[0]; $d = $fr[1]; $w.Write([byte]($(if ($s2 -ge 256) { 0 } else { $s2 }))); $w.Write([byte]($(if ($s2 -ge 256) { 0 } else { $s2 }))); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$d.Length); $w.Write([uint32]$offset); $offset += $d.Length }
    foreach ($fr in $frames) { $w.Write([byte[]]$fr[1]) }
    $w.Flush(); [IO.File]::WriteAllBytes($FolderIco, $out.ToArray())
}

function Invoke-Admin([string]$script) {
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    try { $p = Start-Process powershell -Verb RunAs -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $enc" -PassThru -Wait; return ($p.ExitCode -eq 0) }
    catch { return $false }
}

function Apply-BlueFolders {
    New-BlueFolderIcon
    Write-Host "  Windows te pedira permiso de administrador (una sola vez) para cambiar el icono de las carpetas." -ForegroundColor Cyan
    $ok = Invoke-Admin "New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons' -Force | Out-Null; Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons' -Name '3' -Value '$FolderIco,0'; Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons' -Name '4' -Value '$FolderIco,0'"
    if (-not $ok) { Write-Host "  No se aplico (se cancelo el permiso de administrador)." -ForegroundColor Yellow; return $false }
    Add-Backup ([pscustomobject]@{ id = "blue"; t = "blue" })
    Start-Process ie4uinit.exe -ArgumentList "-show" -WindowStyle Hidden
    $script:NeedExplorerRestart = $true
    return $true
}

# 4: grafica Intel para apps del dia a dia
function Apply-Gpu {
    $gpuKey = "HKCU:\Software\Microsoft\DirectX\UserGpuPreferences"
    $skip = '\\Windows\\|\\NVIDIA|steam|\\Games\\|epic|riot|obs64|Adobe Premiere|DaVinci|Resolve|blender'
    $apps = Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and $_.Path } |
        Select-Object -ExpandProperty Path -Unique | Where-Object { $_ -notmatch $skip } | Sort-Object
    $docked = @()
    $appsDir = Join-Path $Dir "Apps"
    if (Test-Path $appsDir) {
        $ws = New-Object -ComObject WScript.Shell
        $docked = Get-ChildItem $appsDir -Filter *.lnk | ForEach-Object { $ws.CreateShortcut($_.FullName).TargetPath } |
            Where-Object { $_ -and $_ -like "*.exe" -and $_ -notmatch $skip -and $_ -notmatch 'explorer\.exe' }
    }
    $apps = @(@($apps) + @($docked) | Where-Object { $_ } | Sort-Object -Unique)
    if ($apps.Count -eq 0) { Write-Host "  No encontre apps abiertas." ; return }
    Write-Host ""
    Write-Host "  Apps que usaran la grafica Intel (gasta mucho menos que la NVIDIA):" -ForegroundColor Cyan
    for ($i = 0; $i -lt $apps.Count; $i++) {
        $cur = Get-RegValue $gpuKey $apps[$i]
        $m = if ($cur -match 'GpuPreference=1') { "[Intel] " } elseif ($cur -match 'GpuPreference=2') { "[NVIDIA]" } else { "        " }
        Write-Host ("  {0,2}. {1} {2}" -f ($i + 1), $m, [IO.Path]::GetFileNameWithoutExtension($apps[$i]))
    }
    Write-Host "  (Los juegos, editores de video y Steam no aparecen: esos conviene dejarlos con la NVIDIA.)" -ForegroundColor DarkGray
    $ans = Read-Host "  Numeros separados por coma, T = todas, Enter = cancelar"
    if (-not $ans) { return }
    $sel = if ($ans.Trim().ToUpper() -eq "T") { 0..($apps.Count - 1) } else { $ans -split ',' | ForEach-Object { [int]$_.Trim() - 1 } | Where-Object { $_ -ge 0 -and $_ -lt $apps.Count } }
    foreach ($i in $sel) { Set-RegValue $gpuKey $apps[$i] "GpuPreference=1;" "String" }
    Write-Host "  Listo. Cierra y abre esas apps para que tome efecto." -ForegroundColor Green
}

# ---------------- 5: programas que arrancan con Windows ----------------
function Apply-Startup {
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $apprRun = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
    $apprFolder = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
    $items = @()
    $props = Get-ItemProperty $runKey -ErrorAction SilentlyContinue
    if ($props) {
        foreach ($p in $props.PSObject.Properties) {
            if ($p.Name -like "PS*" -or $p.Name -eq "MacDock") { continue }
            $items += [pscustomobject]@{ Name = $p.Name; Cmd = [string]$p.Value; Appr = $apprRun }
        }
    }
    $sf = [Environment]::GetFolderPath("Startup")
    Get-ChildItem $sf -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "desktop.ini" } |
        ForEach-Object { $items += [pscustomobject]@{ Name = $_.Name; Cmd = $_.FullName; Appr = $apprFolder } }
    if ($items.Count -eq 0) { Write-Host "  No hay programas de inicio de tu usuario." ; return }
    while ($true) {
        Write-Host ""
        Write-Host "  Programas que arrancan al prender la PC:" -ForegroundColor Cyan
        for ($i = 0; $i -lt $items.Count; $i++) {
            $b = Get-RegValue $items[$i].Appr $items[$i].Name
            $on = -not ($b -is [byte[]] -and ($b[0] % 2) -eq 1)
            $st = if ($on) { "ARRANCA   " } else { "desactivado" }
            $cmd = $items[$i].Cmd; if ($cmd.Length -gt 60) { $cmd = $cmd.Substring(0, 57) + "..." }
            Write-Host ("  {0,2}. {1}  {2}" -f ($i + 1), $st, $items[$i].Name)
            Write-Host ("        {0}" -f $cmd) -ForegroundColor DarkGray
        }
        $ans = Read-Host "  Numero para activar/desactivar (Enter = volver)"
        if (-not $ans) { return }
        $i = [int]$ans - 1
        if ($i -lt 0 -or $i -ge $items.Count) { continue }
        $it = $items[$i]
        $b = Get-RegValue $it.Appr $it.Name
        $on = -not ($b -is [byte[]] -and ($b[0] % 2) -eq 1)
        $new = [byte[]](0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
        $new[0] = if ($on) { 3 } else { 2 }
        Set-RegValue $it.Appr $it.Name $new "Binary"
    }
}

# ---------------- revertir ----------------
function Revert-All {
    $list = @(Load-Backup)
    if ($list.Count -eq 0) { Write-Host "  No hay nada que revertir." ; return }
    [array]::Reverse($list)
    foreach ($e in $list) {
        try {
            switch ($e.t) {
                "reg" {
                    if ($e.existed) {
                        $v = $e.value; if ($e.kind -eq "Binary") { $v = [byte[]]@($e.value) }
                        if (-not (Test-Path $e.path)) { New-Item -Path $e.path -Force | Out-Null }
                        New-ItemProperty -Path $e.path -Name $e.name -Value $v -PropertyType $e.kind -Force | Out-Null
                    } else { Remove-ItemProperty -Path $e.path -Name $e.name -ErrorAction SilentlyContinue }
                }
                "regkey" { if (-not $e.existed) { Remove-Item -Path $e.path -Recurse -Force -ErrorAction SilentlyContinue; $script:NeedExplorerRestart = $true } }
                "power" {
                    if ($e.value -ne $null) {
                        if ($e.dc) { powercfg /setdcvalueindex SCHEME_CURRENT $e.sub $e.set $e.value | Out-Null } else { powercfg /setacvalueindex SCHEME_CURRENT $e.sub $e.set $e.value | Out-Null }
                    }
                }
                "dock" {
                    $lines = @(); if (Test-Path $DockIni) { $lines = @(Get-Content $DockIni | Where-Object { $_ -notmatch "^$($e.key)=" }) }
                    if ($e.value -ne $null) { $lines += "$($e.key)=$($e.value)" }
                    Set-Content $DockIni $lines
                }
                "anim" { Set-Anim ([int]$e.client) ([int]$e.minmax) }
                "pin" { Unpin-Home $e.path; $script:NeedExplorerRestart = $true }
                "bags" {
                    reg.exe delete "$BagsKey\Bags" /f 2>$null | Out-Null
                    if (Test-Path $e.file) { reg.exe import $e.file 2>$null | Out-Null }
                    $script:NeedExplorerRestart = $true
                }
                "blue" {
                    Invoke-Admin "Remove-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons' -Name '3','4' -ErrorAction SilentlyContinue" | Out-Null
                    Start-Process ie4uinit.exe -ArgumentList "-show" -WindowStyle Hidden
                    $script:NeedExplorerRestart = $true
                }
                "lock" {
                    # si Windows Spotlight estaba activo, ya vuelve solo; si habia una imagen, se restaura
                    if ((Get-RegValue $cdm "RotatingLockScreenEnabled") -ne 1 -and (Test-Path $e.orig)) { Set-LockImage $e.orig }
                }
            }
        } catch { Write-Host "  (no se pudo revertir $($e.id): $($_.Exception.Message))" -ForegroundColor Yellow }
    }
    powercfg /setactive SCHEME_CURRENT | Out-Null
    try { Refresh-Look } catch { }
    Remove-Item $BackupFile -Force
    if (Get-Process MacDock -ErrorAction SilentlyContinue) { Restart-Dock }
    $script:NeedExplorerRestart = $true
    Write-Host "  Listo: todo lo de este menu volvio a como estaba." -ForegroundColor Green
}

# ---------------- menu ----------------
if ($Revertir) { if (Test-Path $BackupFile) { Revert-All }; return }
if ($NoMenu) { return }
while ($true) {
    Clear-Host
    Write-Host ""
    Write-Host "  ==============  AJUSTES DE WINDOWS  ==============" -ForegroundColor Cyan
    Write-Host "  Sin administrador. Todo se puede deshacer con R." -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  BATERIA" -ForegroundColor Yellow
    Write-Host "   1. $(Mark 1) Pantalla a 60 Hz con bateria (144 Hz enchufado, automatico)"
    Write-Host "   2. $(Mark 2) Sin Turbo Boost con bateria (menos calor, bastante mas bateria)"
    Write-Host "   3. $(Mark 3) Hibernar al cerrar la tapa con bateria (no gasta en la noche)"
    Write-Host "   4.          Apps del dia a dia con la grafica Intel en vez de la NVIDIA"
    Write-Host "   5.          Elegir que programas arrancan al prender la PC"
    Write-Host "  11. $(Mark 11) App Nap: apps que no usas en modo eficiencia (como Mac)"
    Write-Host "  12. $(Mark 12) Apps de la tienda sin correr en segundo plano"
    Write-Host "  13. $(Mark 13) Xbox Game Bar sin grabar en segundo plano"
    Write-Host "  30. $(Mark 30) Ahorro de energia automatico desde el 40%"
    Write-Host "  31. $(Mark 31) Con bateria: pantalla se apaga a los 3 min y duerme a los 15"
    Write-Host ""
    Write-Host "  VELOCIDAD" -ForegroundColor Yellow
    Write-Host "  14. $(Mark 14) Arranque sin espera para los programas de inicio"
    Write-Host "  15. $(Mark 15) Menus que se abren al instante"
    Write-Host ""
    Write-Host "  APARIENCIA Y PRODUCTIVIDAD" -ForegroundColor Yellow
    Write-Host "   6. $(Mark 6) Menu de clic derecho completo (sin 'Mostrar mas opciones')"
    Write-Host "   7. $(Mark 7) Ver extensiones de archivo (.ps1, .py, .mp4)"
    Write-Host "   8. $(Mark 8) Historial del portapapeles (Win+V)"
    Write-Host "   9. $(Mark 9) Sin animaciones (todo se siente mas rapido)"
    Write-Host "  16. $(Mark 16) Esquinas activas: arriba-izq = ventanas, abajo-izq = escritorio"
    Write-Host ""
    Write-Host "  ASPECTO MAC" -ForegroundColor Yellow
    Write-Host "  17. $(Mark 17) Color de acento azul de Mac"
    Write-Host "  18. $(Mark 18) Cursor negro estilo Mac"
    Write-Host "  19. $(Mark 19) Silencio: sin sonidos del sistema"
    Write-Host "  20. $(Mark 20) Fondo de pantalla estilo Mac (aurora, hecho por codigo)"
    Write-Host "  21. $(Mark 21) Barra superior estilo Mac (hora, bateria, Wi-Fi, app activa)"
    Write-Host "  22. $(Mark 22) Letras suavizadas como en Mac"
    Write-Host "  23. $(Mark 23) Pantalla de bloqueo con la aurora (sin fotos de Microsoft)"
    Write-Host "  24. $(Mark 24) Cursor un poco mas grande"
    Write-Host "  25. $(Mark 25) Menu Inicio limpio (mas apps fijadas, sin Recomendado)"
    Write-Host "  26. $(Mark 26) Explorador tipo Finder (favoritos en la barra lateral)"
    Write-Host "  27. $(Mark 27) Modo claro de dia y oscuro de noche (automatico)"
    Write-Host "  28. $(Mark 28) Fondo dinamico: cambia con la hora del dia"
    Write-Host "  29. $(Mark 29) Notificaciones tranquilas (sin sonido ni sugerencias)"
    Write-Host "  32. $(Mark 32) Explorador tipo Finder: iconos grandes, sin grupos"
    Write-Host "  33. $(Mark 33) Carpetas azules estilo Mac (pide administrador una vez)"
    Write-Host ""
    Write-Host "  PRIVACIDAD" -ForegroundColor Yellow
    Write-Host "  10. $(Mark 10) Sin publicidad personalizada ni sugerencias de Microsoft"
    Write-Host ""
    Write-Host "   A. Aplicar todo lo recomendado de bateria y velocidad (1-3, 6-8, 10-16, 30, 31)"
    Write-Host "   M. Aplicar todo el aspecto Mac (17 a 28, menos el 27: claro/oscuro va con el boton sol/luna)"
    Write-Host "   R. Revertir todo lo aplicado con este menu"
    Write-Host "   S. Salir"
    Write-Host ""
    $op = (Read-Host "  Elige una opcion (puedes poner varias: 1,2,7)").Trim().ToUpper()
    if ($op -eq "S" -or $op -eq "") { break }
    try {
        if ($op -eq "R") { Revert-All }
        elseif ($op -eq "A") { foreach ($n in 1, 2, 3, 6, 7, 8, 10, 11, 12, 13, 14, 15, 16, 30, 31) { Apply $n } }
        elseif ($op -eq "M") { foreach ($n in (17..26 + 28 + 29 + 32 + 33)) { Apply $n } }
        else { foreach ($n in ($op -split '[, ]+' | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })) { if ($n -ge 1 -and $n -le 33) { Apply $n } } }
    } catch { Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red }
    Read-Host "  Enter para volver al menu" | Out-Null
}

if ($script:NeedExplorerRestart) {
    $r = Read-Host "  Algunos cambios se ven al reiniciar el Explorador (se cierran las carpetas abiertas). Reiniciar ahora? (s/n)"
    if ($r -match '^[sS]') {
        Stop-Process -Name explorer -Force
        Start-Sleep 2
        if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
    }
}
