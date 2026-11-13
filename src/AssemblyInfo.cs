// Identidad del ejecutable: es lo que Windows muestra en Propiedades > Detalles,
// en el Administrador de tareas y en los avisos de seguridad.
// Un .exe sin estos datos (version 0.0.0.0 y sin fabricante) es una de las senales
// que mas peso tienen en los modelos de los antivirus, porque es lo tipico de un
// binario anonimo. Con esto el archivo tiene nombre, version y responsable.
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("MacDock")]
[assembly: AssemblyDescription("Dock, barra superior, Finder, Spotlight y Quick Look estilo Mac para Windows 10/11")]
[assembly: AssemblyCompany("Cristhianmcc")]
[assembly: AssemblyProduct("MacDock")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Cristhianmcc. Licencia MIT.")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("7f2a1c34-5d6b-4e21-9a0f-3c8b6d1e2a45")]
[assembly: AssemblyVersion("3.9.0.0")]
[assembly: AssemblyFileVersion("3.9.0.0")]
[assembly: AssemblyInformationalVersion("3.9.0")]
