using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacDock
{
    // Instalador / desinstalador integrado en el mismo .exe
    static class Setup
    {
        const string Title = "MacDock";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MacDock";

        static string Dir { get { return Program.DataDir; } }
        static string Exe { get { return Path.Combine(Dir, "MacDock.exe"); } }
        static string Backup { get { return Path.Combine(Dir, "respaldo"); } }

        static readonly string[][] BackupKeys = {
            new[] { "advanced", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
            new[] { "personalize", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" },
            new[] { "search", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search" },
            new[] { "stuckrects", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3" },
            new[] { "contentdelivery", @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager" },
        };

        static string AjustesPs1 { get { return Path.Combine(Dir, "ajustes.ps1"); } }
        static string PowerShellExe { get { return Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"); } }
        static string AjustesArgs(string extra) { return "-NoProfile -ExecutionPolicy Bypass -File \"" + AjustesPs1 + "\"" + extra; }

        static void ExtractAjustes()
        {
            try
            {
                using (var s = typeof(Setup).Assembly.GetManifestResourceStream("ajustes.ps1"))
                {
                    if (s == null) return;
                    using (var f = File.Create(AjustesPs1)) s.CopyTo(f);
                }
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        // ---- fuente Inter (licencia libre SIL OFL), instalada solo para este usuario ----
        [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int AddFontResource(string f);
        [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern bool RemoveFontResource(string f);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, int m, IntPtr w, IntPtr l, int f, int t, out IntPtr r);
        static readonly string[][] Fonts = {
            new[] { "Inter-Regular.ttf", "Inter (TrueType)" },
            new[] { "Inter-Medium.ttf", "Inter Medium (TrueType)" },
            new[] { "Inter-SemiBold.ttf", "Inter SemiBold (TrueType)" },
        };
        const string FontsKey = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";
        static string UserFontsDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts"); } }

        static void FontsChanged() { IntPtr r; SendMessageTimeout(new IntPtr(0xffff), 0x1D, IntPtr.Zero, IntPtr.Zero, 2, 2000, out r); }

        static void InstallFonts()
        {
            try
            {
                Directory.CreateDirectory(UserFontsDir);
                var asm = typeof(Setup).Assembly;
                foreach (var f in Fonts)
                {
                    string dst = Path.Combine(UserFontsDir, f[0]);
                    using (var s = asm.GetManifestResourceStream(f[0]))
                    {
                        if (s == null) continue;
                        if (!File.Exists(dst)) using (var o = File.Create(dst)) s.CopyTo(o);
                    }
                    using (var k = Registry.CurrentUser.CreateSubKey(FontsKey)) k.SetValue(f[1], dst);
                    AddFontResource(dst);
                }
                using (var s = asm.GetManifestResourceStream("OFL.txt"))
                    if (s != null) using (var o = File.Create(Path.Combine(Dir, "Inter-licencia-OFL.txt"))) s.CopyTo(o);
                FontsChanged();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        static void UninstallFonts()
        {
            try
            {
                foreach (var f in Fonts)
                {
                    string dst = Path.Combine(UserFontsDir, f[0]);
                    RemoveFontResource(dst);
                    try { using (var k = Registry.CurrentUser.OpenSubKey(FontsKey, true)) if (k != null) k.DeleteValue(f[1], false); } catch { }
                    try { File.Delete(dst); } catch { }
                }
                FontsChanged();
            }
            catch { }
        }

        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) try { File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true); } catch { }
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        static void Reg(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("reg.exe", args) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                p.WaitForExit(10000);
            }
            catch { }
        }

        static void SetDword(string key, string name, int value)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(key)) k.SetValue(name, value, RegistryValueKind.DWord); } catch { }
        }

        static void StopRunningDock()
        {
            IntPtr h = N.FindWindow(null, "MacDockWindow");
            if (h != IntPtr.Zero) N.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero);
            int me = Process.GetCurrentProcess().Id;
            for (int i = 0; i < 30; i++)
            {
                bool any = false;
                foreach (var p in Process.GetProcessesByName("MacDock")) if (p.Id != me) any = true;
                if (!any) return;
                Thread.Sleep(100);
            }
            foreach (var p in Process.GetProcessesByName("MacDock")) if (p.Id != me) try { p.Kill(); } catch { }
            Thread.Sleep(300);
        }

        public static void Install()
        {
            if (MessageBox.Show(
                "Se instalara el dock estilo Mac:\n\n" +
                "  - Dock con tus apps fijadas (iconos reales y efecto lupa)\n" +
                "  - La barra de tareas de Windows se oculta\n" +
                "  - Modo oscuro y menos sugerencias/anuncios de Windows\n" +
                "  - Fuente Inter (estilo Mac, licencia libre) solo para tu usuario\n" +
                "  - Se inicia solo con Windows\n\n" +
                "Antes se hace un respaldo de tu configuracion. Puedes desinstalarlo\n" +
                "cuando quieras desde Configuracion > Aplicaciones o con \"Desinstalar MacDock\".\n\n" +
                "Continuar?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                StopRunningDock();
                Directory.CreateDirectory(Dir);
                Directory.CreateDirectory(Backup);
                // recuperar la configuracion guardada en Imagenes\MacDock\migrar (apps del dock, opciones, respaldos)
                string mig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MacDock", "migrar");
                if (Directory.Exists(mig)) { CopyDir(mig, Dir); try { Directory.Delete(mig, true); } catch { } }

                string self = Application.ExecutablePath;
                if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, Exe, true);
                File.WriteAllText(Exe + ".config",
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<configuration>\r\n" +
                    "  <startup><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\"/></startup>\r\n" +
                    "  <runtime><gcConcurrent enabled=\"false\"/><legacyUnhandledExceptionPolicy enabled=\"1\"/></runtime>\r\n</configuration>\r\n");

                // 1) respaldo (solo la primera vez, para conservar la configuracion original)
                foreach (var k in BackupKeys)
                {
                    string f = Path.Combine(Backup, k[0] + ".reg");
                    if (!File.Exists(f)) Reg("export \"" + k[1] + "\" \"" + f + "\" /y");
                }

                // 2) diseno limpio
                const string adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
                const string per = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
                const string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                SetDword(per, "AppsUseLightTheme", 0);
                SetDword(per, "SystemUsesLightTheme", 0);
                SetDword(per, "EnableTransparency", 1);
                SetDword(adv, "TaskbarAl", 1);
                SetDword(adv, "ShowTaskViewButton", 0);
                SetDword(adv, "TaskbarDa", 0);
                SetDword(adv, "Start_IrisRecommendations", 0);
                SetDword(@"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0);
                SetDword(cdm, "SubscribedContent-338388Enabled", 0);
                SetDword(cdm, "SubscribedContent-338389Enabled", 0);
                SetDword(cdm, "SystemPaneSuggestionsEnabled", 0);

                // 3) inicio automatico + entrada en "Aplicaciones instaladas"
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue("MacDock", "\"" + Exe + "\"");
                using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
                {
                    k.SetValue("DisplayName", "MacDock (dock estilo Mac)");
                    k.SetValue("DisplayVersion", "3.7");
                    k.SetValue("Publisher", "MacDock");
                    k.SetValue("DisplayIcon", Exe);
                    k.SetValue("InstallLocation", Dir);
                    k.SetValue("UninstallString", "\"" + Exe + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", 120, RegistryValueKind.DWord);
                }

                // 4) fuente Inter + menu de ajustes (bateria, apariencia, privacidad) + accesos directos en el menu Inicio
                InstallFonts();
                ExtractAjustes();
                try
                {
                    string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    Shell.CreateLnkArgs(Path.Combine(startMenu, "Desinstalar MacDock.lnk"), Exe, "--uninstall");
                    Shell.CreateLnkArgs(Path.Combine(startMenu, "MacDock.lnk"), Exe, "");
                    Shell.CreateLnkArgs(Path.Combine(startMenu, "Finder.lnk"), Exe, "--finder");
                    Shell.CreateLnkArgs(Path.Combine(startMenu, "Ajustes de Windows (bateria y mas).lnk"), PowerShellExe, AjustesArgs(""));
                }
                catch { }

                Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = true, WorkingDirectory = Dir });
                if (MessageBox.Show("Listo, el dock ya esta funcionando.\n\nTip: clic derecho sobre el dock para ver las opciones.\n\n" +
                    "Quieres abrir ahora los Ajustes de Windows (bateria, Win+V, extensiones, privacidad)?\n" +
                    "Tambien los encuentras despues en el menu Inicio.", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes && File.Exists(AjustesPs1))
                    Process.Start(new ProcessStartInfo(PowerShellExe, AjustesArgs("")) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                MessageBox.Show("No se pudo completar la instalacion:\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void Uninstall()
        {
            if (MessageBox.Show(
                "Se quitara el dock y Windows volvera a como estaba antes de instalarlo\n" +
                "(barra de tareas, tema y sugerencias).\n\n" +
                "El Explorador de Windows se reiniciara un momento (se cierran las carpetas abiertas).\n\n" +
                "Continuar?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                StopRunningDock();
                // deshacer tambien lo aplicado desde "Ajustes de Windows"
                if (File.Exists(Path.Combine(Dir, "ajustes-respaldo.json")) && File.Exists(AjustesPs1))
                {
                    try
                    {
                        var p = Process.Start(new ProcessStartInfo(PowerShellExe, AjustesArgs(" -Revertir")) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                        p.WaitForExit(60000);
                    }
                    catch { }
                    StopRunningDock();
                }
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null) k.DeleteValue("MacDock", false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); } catch { }
                try
                {
                    string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    File.Delete(Path.Combine(startMenu, "Desinstalar MacDock.lnk"));
                    File.Delete(Path.Combine(startMenu, "MacDock.lnk"));
                    File.Delete(Path.Combine(startMenu, "Finder.lnk"));
                    File.Delete(Path.Combine(startMenu, "Ajustes de Windows (bateria y mas).lnk"));
                }
                catch { }

                if (Directory.Exists(Backup))
                    foreach (var f in Directory.GetFiles(Backup, "*.reg")) Reg("import \"" + f + "\"");

                IntPtr tray = N.FindWindow("Shell_TrayWnd", null);
                if (tray != IntPtr.Zero) N.ShowWindow(tray, 5);

                foreach (var p in Process.GetProcessesByName("explorer")) try { p.Kill(); } catch { }
                Thread.Sleep(2500);
                if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");

                UninstallFonts();
                try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MacDock"), true); } catch { }

                // borrar la carpeta del programa cuando este proceso termine
                Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 3 /nobreak >nul & rmdir /s /q \"" + Dir + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });

                MessageBox.Show("MacDock se desinstalo y Windows quedo como antes.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                MessageBox.Show("Hubo un problema al desinstalar:\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
