using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacDock
{
    // Temporizador de concentracion (tecnica Pomodoro): rondas de concentracion y descanso, cada 4 rondas un descanso largo.
    // Un solo reloj para todo MacDock: el widget del escritorio y el del Centro de Notificaciones muestran el mismo tiempo,
    // y sigue corriendo aunque cierres el panel. Al terminar cada fase: aviso estilo Mac y un sonido.
    static class FocusTimer
    {
        public enum Phase { Focus, Break, LongBreak }

        public static Phase Current = Phase.Focus;
        public static bool Running;
        public static int Round = 1;                     // 1..Rounds
        public static int FocusMin = 25, BreakMin = 5, LongMin = 15, Rounds = 4;   // Rounds: concentraciones antes del descanso largo
        public static bool SilenceWhileFocus;            // activar "No molestar" durante la concentracion
        static DateTime endsAt; static TimeSpan left = TimeSpan.FromMinutes(25);
        static Timer timer;
        static bool silencedByUs;

        static string SettingsPath { get { return Path.Combine(Program.DataDir, "concentracion.txt"); } }

        static FocusTimer()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var p = File.ReadAllText(SettingsPath).Trim().Split(',');
                    if (p.Length >= 4) { FocusMin = int.Parse(p[0]); BreakMin = int.Parse(p[1]); LongMin = int.Parse(p[2]); SilenceWhileFocus = p[3] == "1"; }
                    if (p.Length >= 5) Rounds = Math.Max(2, Math.Min(8, int.Parse(p[4])));
                }
            }
            catch { }
            left = TimeSpan.FromMinutes(FocusMin);
            LoadTasks(); LoadToday();
        }

        static void SaveSettings()
        {
            try { Directory.CreateDirectory(Program.DataDir); File.WriteAllText(SettingsPath, FocusMin + "," + BreakMin + "," + LongMin + "," + (SilenceWhileFocus ? 1 : 0) + "," + Rounds); } catch { }
        }

        public static TimeSpan Left { get { return Running ? (endsAt > DateTime.Now ? endsAt - DateTime.Now : TimeSpan.Zero) : left; } }
        public static TimeSpan Total { get { return TimeSpan.FromMinutes(Current == Phase.Focus ? FocusMin : Current == Phase.Break ? BreakMin : LongMin); } }
        public static string PhaseName { get { return Current == Phase.Focus ? "Concentración" : Current == Phase.Break ? "Descanso" : "Descanso largo"; } }
        public static string StateKey { get { var l = Left; return Current + "|" + Running + "|" + Round + "|" + (int)Math.Ceiling(l.TotalSeconds); } }

        // siempre se llama desde el hilo de la interfaz (clics en los widgets)
        public static void Toggle()
        {
            if (Running) { left = Left; Running = false; Silence(false); }
            else
            {
                if (left <= TimeSpan.Zero) left = Total;
                endsAt = DateTime.Now + left; Running = true;
                if (Current == Phase.Focus) Silence(true);
                if (timer == null) { timer = new Timer { Interval = 1000 }; timer.Tick += (o, e) => Check(); }
                timer.Start();
            }
        }

        public static void Reset()
        {
            Running = false; Current = Phase.Focus; Round = 1; left = TimeSpan.FromMinutes(FocusMin);
            Silence(false);
            if (timer != null) timer.Stop();
        }

        public static void Skip() { Finish(false); }

        // elegir la fase a mano (las pestañas de la ventana); si estaba corriendo, sigue corriendo en la nueva
        public static void SetPhase(Phase p)
        {
            if (p == Current) return;
            bool was = Running;
            if (Running) { Running = false; Silence(false); }
            Current = p; left = Total;
            if (p == Phase.Focus && Round > Rounds) Round = 1;
            if (was) Toggle();
        }

        // lo que viene despues de la fase actual, para que siempre se sepa que sigue
        public static Phase Next { get { return Current == Phase.Focus ? (Round >= Rounds ? Phase.LongBreak : Phase.Break) : Phase.Focus; } }
        public static string NameOf(Phase p) { return p == Phase.Focus ? "Concentración" : p == Phase.Break ? "Descanso" : "Descanso largo"; }
        public static int MinutesOf(Phase p) { return p == Phase.Focus ? FocusMin : p == Phase.Break ? BreakMin : LongMin; }
        public static bool Started { get { return Running || left < Total; } }   // ya se empezo esta fase (corriendo o en pausa)

        // cambiar los minutos se nota al instante en la fase actual (aunque ya este corriendo o en pausa)
        public static void SetDurations(int focus, int brk, int lng)
        {
            var before = Total; bool started = Started;
            FocusMin = focus; BreakMin = brk; LongMin = lng; SaveSettings();
            var delta = Total - before;
            if (Running) endsAt += delta;
            else if (started) { left += delta; if (left < TimeSpan.Zero) left = TimeSpan.Zero; }
            else left = Total;
        }

        public static void SetRounds(int n) { Rounds = Math.Max(2, Math.Min(8, n)); if (Round > Rounds) Round = Rounds; SaveSettings(); }

        // ---------- tareas: en que estas trabajando y cuantos pomodoros le has dedicado ----------
        public class Task { public string Text = ""; public bool Done; public int Count; }
        public static readonly System.Collections.Generic.List<Task> Tasks = new System.Collections.Generic.List<Task>();
        public static Task CurrentTask;
        public static int TodayCount;
        static string todayDate = "";

        static string TasksPath { get { return Path.Combine(Program.DataDir, "concentracion-tareas.txt"); } }
        static string TodayPath { get { return Path.Combine(Program.DataDir, "concentracion-hoy.txt"); } }

        static void LoadTasks()
        {
            try
            {
                if (!File.Exists(TasksPath)) return;
                foreach (var line in File.ReadAllLines(TasksPath))
                {
                    var p = line.Split(new[] { '|' }, 4);
                    if (p.Length < 4) continue;
                    var t = new Task { Done = p[1] == "1", Count = int.Parse(p[2]), Text = p[3] };
                    Tasks.Add(t);
                    if (p[0] == "*") CurrentTask = t;
                }
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        public static void SaveTasks()
        {
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                var lines = new System.Collections.Generic.List<string>();
                foreach (var t in Tasks) lines.Add((t == CurrentTask ? "*" : "-") + "|" + (t.Done ? 1 : 0) + "|" + t.Count + "|" + t.Text.Replace("\r", " ").Replace("\n", " "));
                File.WriteAllLines(TasksPath, lines.ToArray());
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        public static void AddTask(string text)
        {
            text = (text ?? "").Trim(); if (text == "") return;
            var t = new Task { Text = text }; Tasks.Add(t);
            if (CurrentTask == null || CurrentTask.Done) CurrentTask = t;
            SaveTasks();
        }
        public static void RemoveTask(Task t) { Tasks.Remove(t); if (CurrentTask == t) CurrentTask = null; SaveTasks(); }
        public static void ToggleDone(Task t)
        {
            t.Done = !t.Done;
            if (t.Done && CurrentTask == t) { CurrentTask = null; foreach (var x in Tasks) if (!x.Done) { CurrentTask = x; break; } }
            SaveTasks();
        }
        public static void SelectTask(Task t) { CurrentTask = CurrentTask == t ? null : t; SaveTasks(); }
        public static void ClearDone() { Tasks.RemoveAll(t => t.Done); SaveTasks(); }

        static void LoadToday()
        {
            todayDate = DateTime.Today.ToString("yyyy-MM-dd"); TodayCount = 0;
            try { if (File.Exists(TodayPath)) { var p = File.ReadAllText(TodayPath).Trim().Split(','); if (p.Length == 2 && p[0] == todayDate) TodayCount = int.Parse(p[1]); } } catch { }
        }
        public static int Today { get { if (todayDate != DateTime.Today.ToString("yyyy-MM-dd")) LoadToday(); return TodayCount; } }

        // una concentracion completa: suma al dia y a la tarea actual
        static void CountPomodoro()
        {
            int n = Today + 1; TodayCount = n;
            try { Directory.CreateDirectory(Program.DataDir); File.WriteAllText(TodayPath, todayDate + "," + n); } catch { }
            if (CurrentTask != null) { CurrentTask.Count++; SaveTasks(); }
        }

        public static void SetSilence(bool on) { SilenceWhileFocus = on; SaveSettings(); if (!on) Silence(false); else if (Running && Current == Phase.Focus) Silence(true); }

        static void Check()
        {
            if (!Running) { timer.Stop(); return; }
            if (DateTime.Now >= endsAt) Finish(true);
        }

        // termina la fase: pasa a la siguiente, avisa y sigue corriendo (como las apps Pomodoro)
        static void Finish(bool notify)
        {
            var prev = Current;
            bool wasFocus = prev == Phase.Focus;
            if (wasFocus && notify) CountPomodoro();
            if (wasFocus) Current = Round >= Rounds ? Phase.LongBreak : Phase.Break;
            else { Current = Phase.Focus; Round = prev == Phase.LongBreak ? 1 : Round + 1; }
            left = Total;
            bool keepRunning = Running;
            Running = false;
            Silence(false);
            if (keepRunning) Toggle();
            if (notify)
            {
                try { SystemSounds.Asterisk.Play(); } catch { }
                if (wasFocus) Banner.Show("¡Buen trabajo!", Current == Phase.LongBreak ? "Toma un descanso largo de " + LongMin + " minutos." : "Descansa " + BreakMin + " minutos.");
                else Banner.Show("A concentrarse", "Ronda " + Round + " de " + Rounds + ": " + FocusMin + " minutos" + (CurrentTask != null ? " en " + CurrentTask.Text : "") + ".");
            }
        }

        // "No molestar" mientras te concentras (solo si tu lo elegiste); se regresa como estaba al pausar o descansar
        static void Silence(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings"))
                {
                    if (k == null) return;
                    if (on && SilenceWhileFocus && Convert.ToInt32(k.GetValue("NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", 1)) != 0)
                    { k.SetValue("NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", 0, RegistryValueKind.DWord); silencedByUs = true; }
                    else if (!on && silencedByUs) { k.SetValue("NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", 1, RegistryValueKind.DWord); silencedByUs = false; }
                }
            }
            catch { }
        }

        public static void Shutdown() { Silence(false); }
    }

    // Aviso estilo Mac: tarjeta arriba a la derecha que se va sola a los segundos (clic para cerrarla).
    class Banner : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        static Banner current;
        readonly string title, text; readonly bool light;
        readonly Timer life = new Timer { Interval = 7000 };

        public static void Show(string title, string text)
        {
            try
            {
                if (current != null && !current.IsDisposed) current.Close();
                current = new Banner(title, text);
                current.Show();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        Banner(string t, string x)
        {
            title = t; text = x;
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) light = k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1; } catch { }
            float s = DeviceDpi / 96f;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; DoubleBuffered = true;
            BackColor = light ? Color.FromArgb(246, 246, 250) : Color.FromArgb(40, 40, 46);
            var wa = Screen.PrimaryScreen.WorkingArea;
            int w = (int)(340 * s), h = (int)(72 * s);
            Bounds = new Rectangle(wa.Right - w - (int)(12 * s), wa.Top + (int)(10 * s), w, h);
            life.Tick += (o, e) => Close();
            Click += (o, e) => Close();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; } }   // sin robar el foco ni salir en Alt+Tab
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
            int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); life.Start(); }
        protected override void OnFormClosed(FormClosedEventArgs e) { life.Stop(); life.Dispose(); base.OnFormClosed(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics; float s = DeviceDpi / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var ic = new RectangleF(14 * s, 16 * s, 40 * s, 40 * s);
            using (var b = new LinearGradientBrush(ic, Color.FromArgb(255, 112, 67), Color.FromArgb(255, 59, 48), 90f))
            using (var p = new GraphicsPath()) { p.AddEllipse(ic); g.FillPath(b, p); }
            using (var f = new Font("Segoe MDL2 Assets", 18f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("", f, Brushes.White, ic, sf);   // cronometro
            Color fg = light ? Color.FromArgb(24, 24, 28) : Color.FromArgb(242, 242, 246), sub = light ? Color.FromArgb(110, 110, 120) : Color.FromArgb(165, 165, 175);
            using (var ft = new Font(UiFont.Semi, 13.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fx = new Font(UiFont.Reg, 12f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var b1 = new SolidBrush(fg)) using (var b2 = new SolidBrush(sub))
            {
                g.DrawString(title, ft, b1, 66 * s, 15 * s);
                g.DrawString(text, fx, b2, new RectangleF(66 * s, 35 * s, Width - 76 * s, 30 * s));
            }
        }
    }

    // ===================== widget de concentracion =====================
    class FocusWidget : DesktopWidget
    {
        protected override int RefreshMs { get { return 1000; } }
        protected override string StateKey() { return FocusTimer.StateKey; }
        RectangleF rPlay, rReset;   // relativos a la tarjeta

        protected override void OnClickAt(PointF p)
        {
            if (rPlay.Contains(p)) FocusTimer.Toggle();
            else if (rReset.Contains(p)) FocusTimer.Reset();
            else { FocusWindow.Open(); return; }   // clic en el resto: abre la ventana con tiempos y tareas
            Redraw(true);
        }

        protected override void DrawContent(Graphics g, RectangleF r)
        {
            float pad = 14 * s;
            bool brk = FocusTimer.Current != FocusTimer.Phase.Focus;
            Color accent = brk ? Green : Color.FromArgb(255, 99, 71);
            using (var b = new SolidBrush(accent)) g.FillEllipse(b, r.X + pad, r.Y + pad + 4 * s, 8 * s, 8 * s);
            using (var f = F(12f, true)) Txt(g, FocusTimer.PhaseName, f, Fg, new RectangleF(r.X + pad + 13 * s, r.Y + pad - 2 * s, r.Width - pad * 2 - 40 * s, 18 * s));
            using (var f = F(10.5f)) Txt(g, FocusTimer.Round + "/" + FocusTimer.Rounds, f, Dim, new RectangleF(r.Right - pad - 30 * s, r.Y + pad - 1 * s, 30 * s, 16 * s), StringAlignment.Far);

            var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height * 0.48f);
            float R = r.Width * 0.25f;
            var left = FocusTimer.Left; var total = FocusTimer.Total;
            float frac = total.TotalSeconds > 0 ? (float)(left.TotalSeconds / total.TotalSeconds) : 0;
            Ring(g, c, R, 6 * s, frac, accent);
            string t = ((int)left.TotalMinutes).ToString("00") + ":" + left.Seconds.ToString("00");
            using (var f = F(20f, true)) Txt(g, t, f, Fg, new RectangleF(c.X - R, c.Y - R, R * 2, R * 2), StringAlignment.Center, StringAlignment.Center);

            // botones: iniciar/pausar y reiniciar
            float bs = 26 * s, by = r.Bottom - pad - bs + 4 * s;
            var play = new RectangleF(c.X - bs - 4 * s, by, bs, bs);
            var reset = new RectangleF(c.X + 4 * s, by, bs, bs);
            rPlay = new RectangleF(play.X - r.X, play.Y - r.Y, play.Width, play.Height);
            rReset = new RectangleF(reset.X - r.X, reset.Y - r.Y, reset.Width, reset.Height);
            using (var b = new SolidBrush(accent)) g.FillEllipse(b, play);
            using (var b = new SolidBrush(light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(45, 255, 255, 255))) g.FillEllipse(b, reset);
            using (var f = new Font("Segoe MDL2 Assets", 11f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(FocusTimer.Running ? "" : "", f, Brushes.White, play, sf);
                using (var b = new SolidBrush(Fg)) g.DrawString("", f, b, reset, sf);   // reiniciar
            }
        }
    }
}
