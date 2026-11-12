<div align="center">

<img src="docs/img/carpeta.png" width="96" alt="MacDock">

# MacDock

**Convierte tu Windows 10/11 en algo que se ve y se siente como macOS, sin instalar programas pesados.**

Dock con efecto lupa · Barra superior · Finder · Spotlight · Quick Look · Botones de colores · Más batería

~**15–20 MB de RAM** en total · ~0 % de CPU en reposo · Sin administrador · 100 % reversible

[**⬇️ Descargar el instalador**](descargas/MacDock-Instalador.zip) · [¿Qué hace?](#qué-incluye) · [Instalar](#instalación) · [Desinstalar](#desinstalar) · [Preguntas](#preguntas-frecuentes)

<img src="docs/img/dock.png" alt="Dock de MacDock">

</div>

---

## ¿Qué es?

MacDock es **un solo programa pequeño (~350 KB)** hecho en C# con el compilador que **ya trae Windows** (.NET Framework). No usa Electron, ni Rainmeter, ni programas de terceros, y no inyecta código en otras apps.

Incluye además un **menú de ajustes** (un script de PowerShell) que cambia la configuración de Windows para que se vea como un Mac y para que **la batería dure más**. Todo lo que cambia se guarda en un respaldo y se puede deshacer con una tecla.

> ⚠️ **Proyecto independiente.** No está afiliado ni respaldado por Apple. No usa imágenes, fuentes ni iconos de Apple: todo (iconos, cursor, fondos) está dibujado por código o es software libre.

---

## Qué incluye

### 🧲 Dock estilo Mac
- Tus apps con sus **iconos reales**, **efecto lupa** al pasar el mouse y un **puntito** bajo las que están abiertas.
- **Rebote** al abrir una app. Clic para abrir, traer al frente o minimizar. Arrastra para reordenar.
- **Arrastra una app al dock** para fijarla, o fíjala en la barra de Windows: aparece sola.
- **Suelta archivos en el dock:** en la Papelera se borran (se pueden recuperar), en una app se abren con ella y en Descargas o Escritorio se mueven ahí.
- Se **esconde solo** cuando una ventana lo tapa: lleva el mouse al borde de abajo y aparece deslizándose.
- La barra de tareas de Windows se oculta (vuelve sola si cierras el dock).

### 📚 Pilas (Stacks)
- **Escritorio** y **Descargas** como pilas: un panel con tus archivos agrupados (Hoy, Ayer, Esta semana…), con **miniaturas** y **buscador**.
- **Entra en las carpetas** sin salir de la pila, **arrastra archivos** a cualquier app y usa **Espacio** para la vista rápida.
- **Aviso de descargas:** el icono rebota y muestra un numerito con lo nuevo.

### 🔝 Barra superior

<img src="docs/img/barra-superior.png" alt="Barra superior">

- Menú del sistema (Inicio, Configuración, Suspender, Reiniciar, Apagar, Bloquear).
- Nombre de la **app activa** (minimizar, maximizar, cerrar).
- **⌃ iconos de la bandeja** (antivirus, VPN, Drive…), **luna/sol** para modo oscuro/claro, Wi-Fi, volumen, batería y fecha/hora.

### 🗂️ Finder

<img src="docs/img/finder.png" width="720" alt="Finder">

- Explorador de archivos propio: barra lateral con favoritos, vista de **iconos con miniaturas** o de **lista**, **ordenar** por nombre/fecha/tipo/tamaño y buscador.
- Copiar, cortar, pegar, renombrar, papelera, arrastrar y soltar, **Quick Look** y "Abrir terminal aquí".
- Se actualiza solo cuando cambia la carpeta. Solo usa memoria mientras está abierto.

### 🔍 Spotlight (Alt + Espacio)

<img src="docs/img/spotlight.png" width="640" alt="Spotlight">

- Busca **apps**, **archivos y carpetas** (usa el índice que Windows ya tiene), **ajustes** de Windows y hace **cálculos** (`25*4` → `100`).

### 👀 Quick Look (Espacio)
- Selecciona un archivo y presiona **Espacio**: vista previa grande de fotos, texto y código, y PDF/Word/Excel con los visores de Windows. Funciona en el Finder, en las pilas **y en el Explorador de Windows**.

### 🔴🟡🟢 Botones de ventana estilo Mac
- Los botones de cerrar, minimizar y maximizar se muestran como los de Mac en **todas las ventanas**: a color en la ventana activa y en gris en las demás.
- Es una capa encima de los botones de Windows: **no modifica las apps**.

### 🎨 Apariencia
- Acento azul, **cursor estilo Mac** (dibujado por código, nítido en cualquier tamaño), fuente **Inter** (libre, muy parecida a la de Mac), letras suavizadas y sin sonidos del sistema.
- **Fondos aurora** que cambian con la hora del día (amanecer, día, atardecer, noche), también en la pantalla de bloqueo.
- **Carpetas azules**, Explorador con iconos grandes, menú Inicio más limpio y escritorio limpio (iconos ocultos, accesibles desde la pila).
- **Esquinas activas:** lleva el mouse a una esquina para ver todas las ventanas o el escritorio.
- **Clic derecho en el escritorio** con estilo Mac, que incluye todas las opciones de Windows (Nuevo, Pegar, Git, NVIDIA…).

<img src="docs/img/fondos.png" alt="Fondos dinámicos">

<img src="docs/img/cursor.png" alt="Cursor">

### 🔋 Batería y rendimiento
| Ajuste | Qué hace |
|---|---|
| Pantalla a 60 Hz con batería | Automático: máxima frecuencia enchufado y 60 Hz con batería (lo que más gasta). |
| Sin Turbo con batería | El procesador no se dispara: menos calor, bastante más autonomía. |
| App Nap | Las apps que no estás usando pasan a modo eficiencia (nunca las que están sonando). |
| Pantalla y suspensión | Con batería: atenúa al minuto, apaga a los 3 y duerme a los 15 (muchas PCs vienen con 2 horas). |
| Hibernar al cerrar la tapa | Con batería, no gasta nada mientras está cerrada. |
| Ahorro de energía al 40 % | Se activa antes (Windows trae 20–30 %). |
| Apps en segundo plano, Game Bar | Dejan de correr y de grabar escondidas. |
| Apagar Docker/WSL | Un clic en el dock libera la RAM y la batería que usa su máquina virtual. |

---

## Consumo

| | RAM | CPU |
|---|---|---|
| Dock + barra superior + botones + Spotlight (en reposo) | **~15–20 MB** | ~0 % |
| Finder / Quick Look | solo mientras están abiertos | — |
| Ajustes de Windows | **0 MB** (es configuración) | — |

Para comparar: un dock basado en Electron suele usar entre 100 y 300 MB.

---

## Instalación

1. [**Descarga el zip**](descargas/MacDock-Instalador.zip) y descomprímelo.
2. Doble clic en **`Instalar MacDock.exe`** y acepta.
   - Si aparece **"Windows protegió su PC"**, haz clic en **Más información → Ejecutar de todas formas**. Solo sale en las compilaciones sin firma digital; las versiones publicadas con firma pasan directo. El código está completo aquí para que lo revises, y el zip trae su huella **SHA256** para comprobar que nadie lo alteró (ver [Firma digital y antivirus](#firma-digital-y-antivirus)).
3. Al terminar te pregunta si quieres abrir los **Ajustes de Windows**:
   - Escribe **`A`** para la batería y la velocidad recomendadas.
   - Escribe **`M`** para todo el aspecto Mac.
   - O elige por número solo lo que quieras.
4. Listo. El dock arranca solo cada vez que enciendes la PC.

**Requisitos:** Windows 10 u 11 de 64 bits. Nada más: .NET Framework 4 ya viene con Windows.

---

## Desinstalar

Cualquiera de estas opciones deja **Windows exactamente como estaba** (barra de tareas, tema, ajustes, fuente, cursor y fondos):
- Doble clic en **`Desinstalar MacDock.exe`**.
- Menú Inicio → **Desinstalar MacDock**.
- Configuración → Aplicaciones → Aplicaciones instaladas → **MacDock** → Desinstalar.

¿Solo quieres deshacer los ajustes? Abre "Ajustes de Windows (bateria y mas)" y escribe **`R`**.

---

## Atajos

| Atajo | Acción |
|---|---|
| **Alt + Espacio** | Spotlight |
| **Espacio** | Quick Look del archivo seleccionado |
| **Win + V** | Historial del portapapeles (se activa en los ajustes) |
| **Shift + clic derecho** | Menú completo de Windows |
| Esquina superior | Ver todas las ventanas |
| Esquina inferior izquierda | Escritorio |
| Finder: **Ctrl+1 / Ctrl+2** | Vista de iconos / lista |
| Finder: **Ctrl+Shift+.** | Mostrar archivos ocultos (que empiezan con punto) |

---

## Preguntas frecuentes

**¿Es seguro?**
Sí. No se inyecta en otras apps, no modifica archivos de Windows y no necesita administrador. La única excepción es el ajuste opcional de **carpetas azules**, que pide permiso una vez porque el icono de carpeta es de todo el sistema. Antes de cambiar algo guarda un respaldo, y el desinstalador lo restaura.

**¿Por qué no se cambian los menús o botones *dentro* de cada app?**
Eso solo se logra inyectando código en cada programa, y es lo que vuelve inestable a Windows y hace saltar a los antivirus. MacDock prefiere capas por encima y configuración oficial de Windows.

**¿Funciona en varias pantallas?**
El dock y la barra superior van en la pantalla principal.

**Al encender, tarda unos segundos en aparecer el estilo.**
Es normal: Windows arranca los programas de inicio unos segundos después de mostrar el escritorio.

**Algo no se ve bien.**
Clic derecho en el dock → **Opciones del dock**: cada función se puede apagar por separado. Y si algo falla, la barra de Windows vuelve sola.

---

## Firma digital y antivirus

MacDock no es un virus, pero **un `.exe` sin firma digital y sin versión en sus propiedades se parece demasiado a un binario anónimo** para los antivirus. Eso, más un puñado de cosas que el programa hace de verdad (poner una entrada de inicio, ocultar la barra de tareas, escuchar el teclado para el atajo de Spotlight, ejecutar un script de ajustes), da un puntaje alto en los modelos de Windows Defender y de vez en cuando sale un aviso.

Lo que ya está hecho en el código y en el build:

- El ejecutable declara **fabricante, producto, versión y copyright** (antes decía `0.0.0.0` y nada más).
- Declara su **manifiesto**: que no pide administrador (`asInvoker`) y para qué versiones de Windows está hecho.
- `build.ps1` sabe **firmar** el instalador y el desinstalador con un certificado de firma de código, y publica la huella **SHA256** del zip.
- Las versiones se firman con **SignPath Foundation**, el programa de firma gratuita para proyectos de código abierto (solicitud en trámite).

Lo que hay que hacer para que pase de frente en cualquier PC está en [docs/firma-y-antivirus.md](docs/firma-y-antivirus.md): cómo firmar, cómo pedirle a Microsoft que corrija el falso positivo y la lista de comprobación antes de publicar cada versión.

---

## Compilar desde el código

No necesitas Visual Studio:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Con firma digital (recomendado para publicar):

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 3.8.1 -Firmar
```

| Parámetro | Para qué |
|---|---|
| `-Version 3.9.0` | Cambia la versión en las propiedades del exe (por defecto toma la de `src/AssemblyInfo.cs`). |
| `-Firmar` | Firma con el certificado de firma de código instalado en el equipo. |
| `-Pfx ruta -PfxPassword clave` | Firma con un `.pfx` en vez de un certificado instalado. |
| `-Thumbprint ...` | Elige un certificado concreto del almacén. |

El resultado queda en `dist\` y en `descargas\MacDock-Instalador.zip`, junto a `MacDock-Instalador.zip.sha256`.

| Archivo | Qué es |
|---|---|
| `src/Dock.cs` | El dock, las pilas, soltar archivos, los atajos y las opciones |
| `src/MenuBar.cs` | La barra superior |
| `src/Finder.cs` | El explorador de archivos |
| `src/Spotlight.cs` | El buscador (Alt + Espacio) |
| `src/QuickLook.cs` | La vista rápida (Espacio) |
| `src/MacButtons.cs` | Los botones de colores sobre las ventanas |
| `src/Stack.cs` | Las pilas del Escritorio y Descargas |
| `src/ShellMenu.cs` | El menú del escritorio con las opciones reales de Windows |
| `src/Saver.cs` | App Nap y esquinas activas |
| `src/Setup.cs` | El instalador y desinstalador |
| `src/ajustes.ps1` | El menú de ajustes de Windows (batería, aspecto, privacidad) |
| `src/cursor-mac.ps1` | El generador del cursor |
| `src/AssemblyInfo.cs` | Fabricante, producto, versión y copyright del ejecutable |
| `src/app.manifest` | Manifiesto: sin administrador, compatible con Windows 10/11 |

---

## Créditos y licencia

- Código: **MIT** — ver [LICENSE](LICENSE).
- Fuente **Inter** © The Inter Project Authors, licencia **SIL Open Font License 1.1** ([src/fonts/OFL.txt](src/fonts/OFL.txt)).
- Hecho con ❤️ por [Cristhianmcc](https://github.com/Cristhianmcc). Si te sirvió, deja una ⭐ y compártelo.
