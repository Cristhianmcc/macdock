# Firma digital y antivirus

Esta guía resuelve las dos cosas distintas que la gente suele llamar "me lo detecta como virus":

| Síntoma | Qué es en realidad | Se arregla con |
|---|---|---|
| Defender dice **"Amenaza encontrada"**, pone el archivo en cuarentena y lo borra | Detección del antivirus (casi siempre un falso positivo del modelo de *machine learning*, nombre con `!ml`) | Enviar el falso positivo a Microsoft + las mejoras de identidad que ya tiene el build |
| Sale **"Windows protegió su PC"** al hacer doble clic | SmartScreen: el archivo no tiene firma digital ni reputación | **Solo** con un certificado de firma de código (no hay truco ni código que lo evite) |

---

## 1. Lo que pasó, con datos

El 11/11/2026 a las 16:56, en este equipo, Windows Defender marcó el archivo
`MacDock-Instalador.zip` **descargado desde internet** y lo puso en cuarentena:

| Dato | Valor |
|---|---|
| Detección | `Trojan:Script/Ulthar.A!ml` |
| Gravedad | 5 (grave) |
| Archivo | `C:\Users\Cris\Downloads\MacDock-Instalador.zip` |
| Origen | `https://raw.githubusercontent.com/Cristhianmcc/macdock/refs/heads/main/descargas/MacDock-Instalador.zip` |
| Tipo de detección | Escaneo de descarga (IOAV), es decir, al bajarlo, no al ejecutarlo |
| Acción | Cuarentena |

Después, con las definiciones del 12/11 (versión 1.463.266.0), **el mismo archivo, con el mismo hash, se escanea limpio**:

```
SHA256 FE6B1A375CCE5B798F43230F331E1119DCA3FB63C75597EB4A8E3347DF26F72D
```

Eso confirma lo que es: **un falso positivo del modelo**, no una firma estática de virus. Aparece, desaparece y puede volver a aparecer en otra PC o con otra versión de definiciones. Por eso "hoy no me lo detecta" no significa que esté arreglado.

## 2. Por qué el modelo lo marca

El programa hace cosas legítimas que, juntas y **sin firma digital**, se parecen mucho al perfil de un binario malicioso:

1. **No está firmado.** Es el factor que más pesa y el único que SmartScreen mira.
2. **No tenía identidad.** Hasta ahora el ejecutable decía versión `0.0.0.0` y no tenía fabricante, producto ni copyright: exactamente la ficha de un binario anónimo empaquetado para que no lo rastreen.
3. **Oculta la barra de tareas** (`Shell_TrayWnd`) para dibujar su propio dock. Es lo que hace cualquier dock, y también lo que hace un programa que quiere tapar la interfaz.
4. **Instala una entrada de inicio** en `HKCU\...\CurrentVersion\Run`, que es el mecanismo número uno de persistencia de malware.
5. **Registra un hook de teclado global** para el atajo Alt + Espacio. Un antivirus no puede distinguir un atajo legítimo de un keylogger sin analizar el comportamiento completo.
6. **Ejecuta un script de PowerShell** (el menú de ajustes) que extrae de sus propios recursos a su carpeta y lanza con `-ExecutionPolicy Bypass`. Un `.exe` que suelta y ejecuta un `.ps1` es el patrón clásico de un *dropper*.
7. **Se distribuye desde `raw.githubusercontent.com`**, un dominio muy usado para repartir malware, dentro de un ZIP con un `.exe` adentro.

**No hay que "esconder" nada de eso.** Empaquetar con UPX, ofuscar o meter el exe en otro ejecutable hace que la detección empeore, porque las tres son técnicas de malware.

## 3. Lo que ya está corregido en el proyecto

| Archivo | Cambio |
|---|---|
| `src/AssemblyInfo.cs` *(nuevo)* | Fabricante, producto, descripción, copyright y versión. Ahora Windows muestra "Cristhianmcc / MacDock / 3.8.0.0" en Propiedades → Detalles, en el Administrador de tareas y en los avisos de seguridad. |
| `src/app.manifest` *(nuevo)* | Manifiesto explícito: `asInvoker` (no pide administrador), compatible con Windows 8.1/10/11, rutas largas. |
| `build.ps1` | Compila con el manifiesto y la identidad, acepta `-Version`, sabe **firmar** (`-Firmar`, `-Pfx`, `-Thumbprint`, variables `MACDOCK_*`), limpia `dist\` para que el zip no arrastre archivos viejos, y publica `MacDock-Instalador.zip.sha256`. |
| `src/Setup.cs` | La versión que se registra en "Aplicaciones instaladas" sale del ensamblado, así no se desincroniza nunca. |
| `README.md`, `src/LEEME.txt` | Explican la firma y cómo verificar la huella SHA256. |

## 4. Cómo hacer que pase de frente (en orden de impacto)

### Paso 1 — Firmar el ejecutable (esto es lo único que quita el aviso de SmartScreen)

El build ya está preparado: cuando tengas el certificado, es un parámetro.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 3.8.1 -Firmar
```

El script busca el certificado de firma de código instalado, firma `Instalar MacDock.exe` y `Desinstalar MacDock.exe` con sello de tiempo, y avisa si el estado no queda en `Valid`. Para comprobarlo a mano:

```powershell
Get-AuthenticodeSignature "dist\Instalar MacDock.exe" | Select-Object Status, SignerCertificate
```

Opciones de certificado (verifica precios y requisitos vigentes antes de decidir, cambian seguido):

| Opción | Costo aproximado | Reputación en SmartScreen | Notas |
|---|---|---|---|
| **SignPath Foundation** | Gratis | Se gana con descargas | Solo proyectos de código abierto con licencia OSI. MacDock es MIT y público, así que califica. Firma en la nube, sin token físico. |
| **Certum Open Source Code Signing** | ~€80–100 al año | Se gana con descargas | Pensado para proyectos open source; se puede usar desde la nube (SimplySign). |
| **Certificado OV** (Sectigo, SSL.com, GlobalSign, DigiCert) | ~USD 200–400 al año | Se gana con descargas | El estándar para software comercial. Token físico o HSM en la nube. |
| **Azure Trusted Signing** (Microsoft) | ~USD 10 al mes | Se gana con descargas | Requiere verificar identidad; conviene revisar si tu caso (persona individual o empresa y país) califica. |
| **Certificado EV** | ~USD 300–600 al año | **Inmediata**, sin esperar descargas | La opción más cara y la única que hace que un archivo nuevo pase sin ningún aviso desde el primer día. |

Con un OV, el aviso no desaparece el primer día: SmartScreen le da reputación al *hash* del archivo a medida que se descarga sin incidentes. Con un EV, la reputación viene del certificado.

#### Aplicar a SignPath Foundation (gratis, es el camino para este proyecto)

Es la vía para que **el que descarga desde GitHub no vea el aviso**, sin pagar. SignPath firma el archivo en su nube, así que no hace falta token físico ni comprar nada.

1. Entra a <https://signpath.org/apply> y aplica con el repositorio público de MacDock.
2. Ten a mano: repositorio (`https://github.com/Cristhianmcc/macdock`), licencia (`MIT`, en `LICENSE`) y una descripción corta del proyecto.
3. Texto para el formulario:

```text
Project:      MacDock
Repository:   https://github.com/Cristhianmcc/macdock
License:      MIT (OSI-approved), file LICENSE in the repository root
Distribution: GitHub Releases - a ZIP containing the per-user installer and uninstaller

Description
MacDock is a free, open-source Windows app that brings a macOS-style desktop to
Windows 10/11: dock with magnification, top menu bar, file browser, Spotlight-like
search, Quick Look and window buttons. It is written in C# for .NET Framework 4
(compiled with the in-box csc.exe), the binary is ~470 KB, it has no external
dependencies, no telemetry and no network access. It installs per-user without
administrator rights and is fully reversible from its uninstaller.

Why signing is needed
The binaries are currently unsigned. Users downloading from GitHub get the
SmartScreen "Windows protected your PC" warning, and Microsoft Defender has
already produced a false positive on a release ZIP (Trojan:Script/Ulthar.A!ml,
2026-11-11). Signing the release artifacts removes the warning and reduces the
false positives.

Commercial activity
None. The project is free and MIT-licensed; there is no paid version, no
advertising and no commercial distribution.
```

4. Cuando te aprueben: crean el proyecto, la *signing policy* y la configuración de artefacto; tú guardas el token de API como secreto de GitHub. A partir de ahí, cada release se firma solo en el flujo de trabajo de GitHub Actions.
5. Confirma en su web los requisitos vigentes (proyecto público, licencia OSI y sin uso comercial) antes de aplicar.

Mientras no esté aprobado, `build.ps1 -Firmar` sigue sirviendo si algún día compras un certificado: es el mismo comando y el mismo resultado.

### Paso 2 — Pedirle a Microsoft que corrija el falso positivo

Es gratis y es el trámite oficial. Tarda entre 24 y 72 horas y no requiere tener certificado.

1. Entra a <https://www.microsoft.com/en-us/wdsi/filesubmission>.
2. Elige **"Software developer"** → **"My software is being incorrectly detected"**.
3. Sube el ZIP (o el `.exe`) y completa el formulario con este texto:

```text
Microsoft Defender Antivirus false positive report

Product:  MacDock - open-source Windows UI app (MIT license)
Project:  https://github.com/Cristhianmcc/macdock
Author:   Cristhianmcc (individual developer)

Detected file:      MacDock-Instalador.zip
SHA256:             <pega aquí el hash de descargas\MacDock-Instalador.zip.sha256>
Detection name:     Trojan:Script/Ulthar.A!ml
Defender platform:  4.18.26100.4   Definitions: 1.463.266.0
First seen:         2026-11-11 16:56 (UTC-5)
Download URL:       https://github.com/Cristhianmcc/macdock/releases

What the program does
MacDock draws a macOS-style dock, top menu bar, file browser and search box on
Windows 10/11. The ZIP contains two copies of one .exe (~470 KB, C#/.NET
Framework 4, compiled with the in-box csc.exe): "Instalar MacDock.exe" installs
and "Desinstalar MacDock.exe" uninstalls. It runs per-user with no elevation and
never injects code into other processes, disables security software, or
downloads anything at runtime. Specifically it:

  - copies itself to %LOCALAPPDATA%\MacDock
  - adds one HKCU\Software\Microsoft\Windows\CurrentVersion\Run entry so the
    dock starts with Windows (removed on uninstall)
  - registers a HKCU\...\Uninstall entry so it can be removed from Settings
  - installs the open-source Inter font (SIL OFL) for the current user
  - carries an embedded PowerShell script that applies documented Windows
    settings (theme, taskbar, power, privacy) and reverts them from a JSON
    backup; the uninstaller restores the original settings

Why a heuristic/ML model may score it highly
  - the binaries are not code-signed (no certificate yet)
  - it hides/shows the taskbar window to draw its own dock
  - it registers a low-level keyboard hook for its global Alt+Space hotkey
  - it runs the embedded PowerShell script it writes to its own folder

Request
Please re-analyze the file and remove the detection if it is not malware. The
full source is public and the build is reproducible with build.ps1.
```

4. Guarda el número de caso. Si vuelve a aparecer el aviso, responde en ese mismo caso con el nuevo hash; no abras uno nuevo cada vez.

### Paso 3 — Publicar donde no parezca una descarga rara

- Sube el ZIP como **adjunto de un Release de GitHub** (`github.com/Cristhianmcc/macdock/releases`), no al repositorio ni a `raw.githubusercontent.com`, y enlaza la página del release desde el README.
- Adjunta siempre `MacDock-Instalador.zip.sha256` y el texto va en las notas del release.
- No recompiles una versión ya publicada: **cada compilación cambia el hash y hace perder la reputación ganada**. Compila una vez, publica esa, y sube de versión cuando haya cambios.

### Paso 4 — Ver el panorama completo antes de publicar

Sube el ZIP a <https://www.virustotal.com> y mira qué motores lo marcan:

- **0–2 detecciones de 70** con nombres terminados en `!ml`, `Heur`, `Gen` o `Wacatac`: falso positivo típico. Usa el texto del paso 2 con cada fabricante que lo marque (VirusTotal tiene el enlace "Report false positive" en cada detección).
- Detecciones de muchos motores con nombres concretos: hay algo real que revisar; no lo publiques.

## 5. Lista de comprobación antes de cada publicación

1. `powershell -ExecutionPolicy Bypass -File build.ps1 -Version X.Y.Z -Firmar`
2. `Get-AuthenticodeSignature "dist\Instalar MacDock.exe"` → `Status: Valid`
3. Propiedades del exe → Detalles: fabricante, producto y versión correctos.
4. VirusTotal del zip → 0 detecciones (o solo `!ml` aislados ya reportados).
5. Instalar en una **máquina virtual limpia** de Windows 11, sin exclusiones ni Defender desactivado, y desinstalar para comprobar que todo vuelve como estaba.
6. Subir el ZIP y el `.sha256` al release de GitHub y actualizar el enlace del README.

## Dudas y supuestos

- El precio y los requisitos de cada certificado son aproximados y cambian; hay que confirmarlos con el emisor antes de pagar.
- La detección `Trojan:Script/Ulthar.A!ml` no se reprodujo de forma local con las definiciones actuales (el mismo archivo escanea limpio), así que no se puede saber cuál de los siete puntos del apartado 2 pesó más en el modelo. Los siete están ahí y se corrigen igual: firma, identidad y trámite de falso positivo.
- No se puede prometer "cero avisos para siempre": los modelos de los antivirus cambian solos. Lo que sí queda resuelto de raíz es el aviso de SmartScreen (con firma) y la identidad del binario.
