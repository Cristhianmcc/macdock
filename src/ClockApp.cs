using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace MacDock
{
    // Motor del Reloj: alarmas, temporizador, cronometro y ciudades. Vive mientras MacDock este abierto,
    // asi las alarmas y el temporizador suenan aunque la ventana del Reloj este cerrada.
    static class ClockEngine
    {
        public class Alarm { public int H = 7, M; public string Label = ""; public bool On = true; public int Repeat; public string Fired = ""; }   // Repeat: 0 una vez, 1 diario, 2 lun-vie
        public class City { public string Name, Zone; }

        public static readonly List<Alarm> Alarms = new List<Alarm>();
        public static readonly List<City> Cities = new List<City>();

        // temporizador
        public static TimeSpan TimerTotal = TimeSpan.FromMinutes(5), timerLeft = TimeSpan.FromMinutes(5);
        public static bool TimerRunning, TimerActive;   // Active: empezado (corriendo o en pausa)
        static DateTime timerEnds;
        public static TimeSpan TimerLeft { get { return TimerRunning ? (timerEnds > DateTime.Now ? timerEnds - DateTime.Now : TimeSpan.Zero) : timerLeft; } }

        // cronometro
        public static readonly Stopwatch Watch = new Stopwatch();
        public static readonly List<TimeSpan> Laps = new List<TimeSpan>();

        static Timer tick; static int ringLeft;
        static string AlarmsPath { get { return Path.Combine(Program.DataDir, "alarmas.txt"); } }
        static string CitiesPath { get { return Path.Combine(Program.DataDir, "relojes.txt"); } }

        public static readonly string[][] KnownCities =
        {
            new[] { "Ciudad de México", "Central Standard Time (Mexico)" }, new[] { "Tijuana", "Pacific Standard Time (Mexico)" },
            new[] { "Cancún", "Eastern Standard Time (Mexico)" }, new[] { "Bogotá", "SA Pacific Standard Time" },
            new[] { "Lima", "SA Pacific Standard Time" }, new[] { "Caracas", "Venezuela Standard Time" },
            new[] { "Santiago", "Pacific SA Standard Time" }, new[] { "Buenos Aires", "Argentina Standard Time" },
            new[] { "São Paulo", "E. South America Standard Time" }, new[] { "Nueva York", "Eastern Standard Time" },
            new[] { "Chicago", "Central Standard Time" }, new[] { "Los Ángeles", "Pacific Standard Time" },
            new[] { "Londres", "GMT Standard Time" }, new[] { "Madrid", "Romance Standard Time" },
            new[] { "París", "Romance Standard Time" }, new[] { "Berlín", "W. Europe Standard Time" },
            new[] { "Dubái", "Arabian Standard Time" }, new[] { "Shanghái", "China Standard Time" },
            new[] { "Seúl", "Korea Standard Time" }, new[] { "Tokio", "Tokyo Standard Time" },
            new[] { "Sídney", "AUS Eastern Standard Time" },
        };

        public static void Start()
        {
            if (tick != null) return;
            Load();
            tick = new Timer { Interval = 1000 };
            tick.Tick += (o, e) => { try { Check(); } catch (Exception ex) { Program.Log(ex); } };
            tick.Start();
        }

        static void Load()
        {
            try
            {
                if (File.Exists(AlarmsPath))
                    foreach (var line in File.ReadAllLines(AlarmsPath))
                    {
                        var p = line.Split(new[] { '|' }, 5);
                        if (p.Length == 5) Alarms.Add(new Alarm { On = p[0] == "1", H = int.Parse(p[1]), M = int.Parse(p[2]), Repeat = int.Parse(p[3]), Label = p[4] });
                    }
                if (File.Exists(CitiesPath))
                {
                    foreach (var line in File.ReadAllLines(CitiesPath))
                    { var p = line.Split('|'); if (p.Length == 2) Cities.Add(new City { Name = p[0], Zone = p[1] }); }
                }
                else foreach (var n in new[] { "Nueva York", "Madrid", "Tokio" }) foreach (var k in KnownCities) if (k[0] == n) Cities.Add(new City { Name = k[0], Zone = k[1] });
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                var a = new List<string>(); foreach (var x in Alarms) a.Add((x.On ? 1 : 0) + "|" + x.H + "|" + x.M + "|" + x.Repeat + "|" + x.Label.Replace("|", " "));
                File.WriteAllLines(AlarmsPath, a.ToArray());
                var c = new List<string>(); foreach (var x in Cities) c.Add(x.Name + "|" + x.Zone);
                File.WriteAllLines(CitiesPath, c.ToArray());
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        public static DateTime? TimeIn(City c)
        {
            try { return TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById(c.Zone)); }
            catch { return null; }
        }

        // ---- temporizador ----
        public static void TimerStart() { if (!TimerActive) timerLeft = TimerTotal; if (timerLeft <= TimeSpan.Zero) return; timerEnds = DateTime.Now + timerLeft; TimerRunning = true; TimerActive = true; }
        public static void TimerPause() { timerLeft = TimerLeft; TimerRunning = false; }
        public static void TimerCancel() { TimerRunning = false; TimerActive = false; timerLeft = TimerTotal; }

        // ---- cronometro ----
        public static void Lap() { if (Watch.IsRunning) Laps.Insert(0, Watch.Elapsed); }
        public static void WatchReset() { Watch.Reset(); Laps.Clear(); }

        static void Check()
        {
            var now = DateTime.Now;
            if (ringLeft > 0) { ringLeft--; try { SystemSounds.Exclamation.Play(); } catch { } }

            if (TimerRunning && now >= timerEnds)
            {
                TimerRunning = false; TimerActive = false; timerLeft = TimerTotal;
                Ring("Temporizador", "Se terminó el tiempo (" + ClockApp.Dur(TimerTotal) + ").");
            }

            string key = now.ToString("yyyy-MM-dd HH:mm");
            bool changed = false;
            foreach (var a in Alarms)
            {
                if (!a.On || a.H != now.Hour || a.M != now.Minute || a.Fired == key) continue;
                if (a.Repeat == 2 && (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)) continue;
                a.Fired = key;
                if (a.Repeat == 0) { a.On = false; changed = true; }
                Ring("Alarma " + a.H.ToString("00") + ":" + a.M.ToString("00"), a.Label != "" ? a.Label : "Es hora.");
            }
            if (changed) Save();
        }

        static void Ring(string title, string text)
        {
            ringLeft = 5;
            try { SystemSounds.Exclamation.Play(); } catch { }
            Banner.Show(title, text);
        }
    }

    // Ventana "Reloj" como la de macOS Ventura: reloj mundial, alarmas, cronometro y temporizador.
    class ClockApp : MacWindow
    {
        static ClockApp current;
        static readonly System.Globalization.CultureInfo Es = new System.Globalization.CultureInfo("es-MX");
        public static int Tab;   // se recuerda la ultima pestaña
        readonly Timer tick = new Timer { Interval = 1000 };
        readonly TextBox label;
        int newH = 7, newM = 0, newRepeat = 1;
        RectangleF listArea; float listH;

        public static void Open()
        {
            try
            {
                ClockEngine.Start();
                if (current != null && !current.IsDisposed)
                {
                    if (current.WindowState == FormWindowState.Minimized) current.WindowState = FormWindowState.Normal;
                    current.Activate(); return;
                }
                current = new ClockApp();
                current.FormClosed += (o, e) => { current = null; };
                current.Show();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        ClockApp() : base("Reloj", 640, 520)
        {
            label = MakeBox("Nombre (opcional)");
            label.MaxLength = 60;
            label.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddAlarm(); } };
            tick.Tick += (o, e) => Invalidate();
            tick.Start();
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { tick.Stop(); tick.Dispose(); base.OnFormClosed(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!listArea.Contains(e.Location)) return;
            int max = (int)Math.Max(0, listH - listArea.Height);
            scroll = Math.Max(0, Math.Min(max, scroll - Math.Sign(e.Delta) * (int)(40 * s)));
            Invalidate();
        }

        public static string Dur(TimeSpan t)
        {
            if (t.TotalHours >= 1) return (int)t.TotalHours + " h " + t.Minutes + " min";
            if (t.Minutes > 0 && t.Seconds > 0) return t.Minutes + " min " + t.Seconds + " s";
            if (t.Minutes > 0) return t.Minutes + " min";
            return t.Seconds + " s";
        }

        static string Hms(TimeSpan t)
        {
            return (t.TotalHours >= 1 ? ((int)t.TotalHours).ToString() + ":" : "") + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        void AddAlarm()
        {
            ClockEngine.Alarms.Add(new ClockEngine.Alarm { H = newH, M = newM, Repeat = newRepeat, Label = label.Text.Trim(), On = true });
            ClockEngine.Alarms.Sort((a, b) => (a.H * 60 + a.M).CompareTo(b.H * 60 + b.M));
            ClockEngine.Save(); label.Text = ""; Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            g.Clear(Bg);
            float pad = 16 * s;
            var r = new RectangleF(pad, pad, ClientSize.Width - pad * 2, ClientSize.Height - pad * 2);
            float sw = Math.Min(r.Width, 460 * s);
            Segments(g, new RectangleF(r.X + (r.Width - sw) / 2, r.Y, sw, 30 * s), new[] { "Reloj mundial", "Alarmas", "Cronómetro", "Temporizador" }, Tab,
                i => { Tab = i; scroll = 0; });
            var body = new RectangleF(r.X, r.Y + 30 * s + 14 * s, r.Width, r.Height - 44 * s);
            label.Visible = Tab == 1;
            tick.Interval = Tab == 2 && ClockEngine.Watch.IsRunning ? 50 : 1000;
            switch (Tab)
            {
                case 0: DrawWorld(g, body); break;
                case 1: DrawAlarms(g, body); break;
                case 2: DrawWatch(g, body); break;
                default: DrawTimer(g, body); break;
            }
        }

        // ---------- reloj mundial ----------
        void DrawWorld(Graphics g, RectangleF r)
        {
            var add = new RectangleF(r.Right - 150 * s, r.Y, 150 * s, 30 * s);
            using (var f = F(13f)) Txt(g, "Hora local: " + DateTime.Now.ToString("HH:mm") + "  ·  " + DateTime.Now.ToString("dddd d 'de' MMMM", Es), f, Sub, new RectangleF(r.X, r.Y, r.Width - 160 * s, 30 * s));
            Button(g, add, "+ Agregar ciudad", () => CityMenu(Point.Round(new PointF(add.X, add.Bottom + 4 * s))), true);

            var list = new RectangleF(r.X, r.Y + 42 * s, r.Width, r.Height - 42 * s);
            Card(g, list);
            listArea = RectangleF.Inflate(list, -4 * s, -4 * s);
            float rowH = 72 * s;
            listH = ClockEngine.Cities.Count * rowH;
            if (ClockEngine.Cities.Count == 0)
            {
                using (var f = F(13f)) Txt(g, "Agrega ciudades para ver qué hora es allá.", f, Sub, list, StringAlignment.Center);
                return;
            }
            var st = g.Save(); g.SetClip(listArea);
            float y = listArea.Y - scroll; var now = DateTime.Now;
            foreach (var c in ClockEngine.Cities.ToArray())
            {
                var city = c;
                var row = new RectangleF(listArea.X + 12 * s, y, listArea.Width - 24 * s, rowH); y += rowH;
                if (row.Bottom < listArea.Y || row.Y > listArea.Bottom) continue;
                var t = ClockEngine.TimeIn(city);
                Analog(g, new PointF(row.X + 26 * s, row.Y + rowH / 2), 24 * s, t ?? now);
                string sub = "Zona no disponible";
                if (t.HasValue)
                {
                    int dd = (t.Value.Date - now.Date).Days;
                    double off = Math.Round((t.Value - now).TotalHours * 2) / 2;
                    sub = (dd == 0 ? "Hoy" : dd > 0 ? "Mañana" : "Ayer") + ", " + (off == 0 ? "misma hora" : (off > 0 ? "+" : "−") + Math.Abs(off) + " h");
                }
                using (var f = F(15f, true)) Txt(g, city.Name, f, Fg, new RectangleF(row.X + 62 * s, row.Y + 14 * s, row.Width - 260 * s, 22 * s));
                using (var f = F(12f)) Txt(g, sub, f, Sub, new RectangleF(row.X + 62 * s, row.Y + 38 * s, row.Width - 260 * s, 18 * s));
                using (var f = F(28f)) Txt(g, t.HasValue ? t.Value.ToString("HH:mm") : "--:--", f, Fg, new RectangleF(row.Right - 190 * s, row.Y, 150 * s, rowH), StringAlignment.Far);
                var del = new RectangleF(row.Right - 26 * s, row.Y + (rowH - 24 * s) / 2, 24 * s, 24 * s);
                using (var fi = Icons(10f)) Txt(g, "", fi, Sub, del, StringAlignment.Center);
                if (del.Y >= listArea.Y && del.Bottom <= listArea.Bottom) Hit(del, () => { ClockEngine.Cities.Remove(city); ClockEngine.Save(); });
                if (y < listArea.Bottom) using (var pen = new Pen(CardBorder)) g.DrawLine(pen, row.X + 62 * s, row.Bottom, row.Right, row.Bottom);
            }
            g.Restore(st);
        }

        void CityMenu(Point at)
        {
            var m = MenuStyle.New(light);
            foreach (var k in ClockEngine.KnownCities)
            {
                var kk = k; bool has = ClockEngine.Cities.Exists(c => c.Name == kk[0]);
                var mi = new ToolStripMenuItem(kk[0]) { Enabled = !has };
                mi.Click += (o, e) => { ClockEngine.Cities.Add(new ClockEngine.City { Name = kk[0], Zone = kk[1] }); ClockEngine.Save(); Invalidate(); };
                m.Items.Add(mi);
            }
            m.Show(this, at);
        }

        void Analog(Graphics g, PointF c, float R, DateTime t)
        {
            bool night = t.Hour < 6 || t.Hour >= 19;
            Color face = night ? Color.FromArgb(28, 28, 32) : Color.White, hand = night ? Color.White : Color.FromArgb(28, 28, 32);
            using (var b = new SolidBrush(face)) g.FillEllipse(b, c.X - R, c.Y - R, R * 2, R * 2);
            using (var pen = new Pen(CardBorder)) g.DrawEllipse(pen, c.X - R, c.Y - R, R * 2, R * 2);
            Action<double, float, float, Color> line = (ang, len, w, col) =>
            {
                double a = (ang - 90) * Math.PI / 180;
                using (var pen = new Pen(col, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, c.X, c.Y, c.X + (float)Math.Cos(a) * len, c.Y + (float)Math.Sin(a) * len);
            };
            line((t.Hour % 12 + t.Minute / 60.0) * 30, R * 0.5f, 2.4f * s, hand);
            line((t.Minute + t.Second / 60.0) * 6, R * 0.75f, 1.8f * s, hand);
            line(t.Second * 6, R * 0.8f, 1f * s, Color.FromArgb(255, 149, 0));
            using (var b = new SolidBrush(Color.FromArgb(255, 149, 0))) g.FillEllipse(b, c.X - 2 * s, c.Y - 2 * s, 4 * s, 4 * s);
        }

        // ---------- alarmas ----------
        void DrawAlarms(Graphics g, RectangleF r)
        {
            // nueva alarma
            float formH = 150 * s;
            var fc = new RectangleF(r.X, r.Y, r.Width, formH);
            Card(g, fc);
            float ip = 16 * s;
            // hora grande con flechas arriba/abajo, como la rueda de Mac
            var timeR = new RectangleF(fc.X + ip, fc.Y + ip, 190 * s, formH - ip * 2);
            float half = timeR.Width / 2 - 8 * s;
            var hr = new RectangleF(timeR.X, timeR.Y + 28 * s, half, timeR.Height - 56 * s);
            var mr = new RectangleF(timeR.Right - half, hr.Y, half, hr.Height);
            using (var f = F(40f)) { Txt(g, newH.ToString("00"), f, Fg, hr, StringAlignment.Center); Txt(g, newM.ToString("00"), f, Fg, mr, StringAlignment.Center); }
            using (var f = F(36f)) Txt(g, ":", f, Sub, new RectangleF(hr.Right, hr.Y, mr.X - hr.Right, hr.Height), StringAlignment.Center);
            float ab = 26 * s;
            IconButton(g, new RectangleF(hr.X + (half - ab) / 2, timeR.Y, ab, ab), "", () => newH = (newH + 1) % 24);
            IconButton(g, new RectangleF(hr.X + (half - ab) / 2, timeR.Bottom - ab, ab, ab), "", () => newH = (newH + 23) % 24);
            IconButton(g, new RectangleF(mr.X + (half - ab) / 2, timeR.Y, ab, ab), "", () => newM = (newM + 5) % 60);
            IconButton(g, new RectangleF(mr.X + (half - ab) / 2, timeR.Bottom - ab, ab, ab), "", () => newM = (newM + 55) % 60);
            Hit(hr, () => newH = (newH + 1) % 24);
            Hit(mr, () => newM = (newM + 1) % 60);

            float fx = timeR.Right + 20 * s, fw = fc.Right - ip - fx;
            var inR = new RectangleF(fx, fc.Y + ip, fw, 32 * s);
            using (var p = Round(inR, 8 * s)) using (var b = new SolidBrush(Soft)) g.FillPath(b, p);
            var bb = Rectangle.Round(new RectangleF(inR.X + 10 * s, inR.Y + (inR.Height - label.PreferredHeight) / 2, inR.Width - 20 * s, label.PreferredHeight));
            if (label.Bounds != bb) label.Bounds = bb;
            Segments(g, new RectangleF(fx, inR.Bottom + 12 * s, fw, 30 * s), new[] { "Una vez", "Diario", "Lun a vie" }, newRepeat, i => newRepeat = i);
            Button(g, new RectangleF(fx, fc.Bottom - ip - 32 * s, fw, 32 * s), "Agregar alarma", AddAlarm, true, Color.FromArgb(255, 149, 0));

            // lista
            var list = new RectangleF(r.X, fc.Bottom + 12 * s, r.Width, r.Bottom - fc.Bottom - 12 * s);
            Card(g, list);
            listArea = RectangleF.Inflate(list, -4 * s, -4 * s);
            float rowH = 64 * s;
            listH = ClockEngine.Alarms.Count * rowH;
            if (ClockEngine.Alarms.Count == 0)
            {
                using (var f = F(13f)) Txt(g, "Sin alarmas. Suenan aunque cierres esta ventana, mientras MacDock esté abierto.", f, Sub, RectangleF.Inflate(list, -20 * s, 0), StringAlignment.Center);
                return;
            }
            var st = g.Save(); g.SetClip(listArea);
            float y = listArea.Y - scroll;
            foreach (var a in ClockEngine.Alarms.ToArray())
            {
                var al = a;
                var row = new RectangleF(listArea.X + 14 * s, y, listArea.Width - 28 * s, rowH); y += rowH;
                if (row.Bottom < listArea.Y || row.Y > listArea.Bottom) continue;
                Color c = al.On ? Fg : Sub;
                using (var f = F(30f)) Txt(g, al.H.ToString("00") + ":" + al.M.ToString("00"), f, c, new RectangleF(row.X, row.Y, 110 * s, rowH));
                string rep = al.Repeat == 0 ? "Una vez" : al.Repeat == 1 ? "Todos los días" : "Lunes a viernes";
                using (var f = F(12.5f)) Txt(g, (al.Label != "" ? al.Label + "  ·  " : "") + rep, f, Sub, new RectangleF(row.X + 116 * s, row.Y, row.Width - 116 * s - 100 * s, rowH));
                var sw = new RectangleF(row.Right - 84 * s, row.Y + (rowH - 24 * s) / 2, 42 * s, 24 * s);
                Switch(g, sw, al.On, () => { al.On = !al.On; ClockEngine.Save(); });
                var del = new RectangleF(row.Right - 26 * s, row.Y + (rowH - 24 * s) / 2, 24 * s, 24 * s);
                using (var fi = Icons(10f)) Txt(g, "", fi, Sub, del, StringAlignment.Center);
                if (del.Y >= listArea.Y && del.Bottom <= listArea.Bottom) Hit(del, () => { ClockEngine.Alarms.Remove(al); ClockEngine.Save(); });
                if (y < listArea.Bottom) using (var pen = new Pen(CardBorder)) g.DrawLine(pen, row.X, row.Bottom, row.Right, row.Bottom);
            }
            g.Restore(st);
        }

        // interruptor verde estilo Mac
        void Switch(Graphics g, RectangleF r, bool on, Action a)
        {
            using (var p = Round(r, r.Height / 2)) using (var b = new SolidBrush(on ? Green : Soft)) g.FillPath(b, p);
            float k = r.Height - 4 * s;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, on ? r.Right - k - 2 * s : r.X + 2 * s, r.Y + 2 * s, k, k);
            Hit(r, a);
        }

        // ---------- cronometro ----------
        void DrawWatch(Graphics g, RectangleF r)
        {
            var w = ClockEngine.Watch; var el = w.Elapsed;
            string big = (el.TotalHours >= 1 ? ((int)el.TotalHours) + ":" : "") + el.Minutes.ToString("00") + ":" + el.Seconds.ToString("00") + "," + (el.Milliseconds / 10).ToString("00");
            var top = new RectangleF(r.X, r.Y, r.Width, 150 * s);
            using (var f = F(64f)) Txt(g, big, f, Fg, new RectangleF(top.X, top.Y, top.Width, 100 * s), StringAlignment.Center);
            float bw = 120 * s, by = top.Y + 108 * s, cx = r.X + r.Width / 2;
            bool running = w.IsRunning;
            Button(g, new RectangleF(cx - bw - 8 * s, by, bw, 34 * s), running ? "Vuelta" : "Reiniciar", running ? (Action)ClockEngine.Lap : ClockEngine.WatchReset, false, null, running || el > TimeSpan.Zero);
            Button(g, new RectangleF(cx + 8 * s, by, bw, 34 * s), running ? "Detener" : "Iniciar", () => { if (w.IsRunning) w.Stop(); else w.Start(); }, true, running ? Red : Green);

            var list = new RectangleF(r.X, top.Bottom + 10 * s, r.Width, r.Bottom - top.Bottom - 10 * s);
            Card(g, list);
            listArea = RectangleF.Inflate(list, -4 * s, -4 * s);
            float rowH = 36 * s;
            int n = ClockEngine.Laps.Count;
            listH = (n + (running || n > 0 ? 1 : 0)) * rowH;
            if (n == 0 && !running)
            {
                using (var f = F(13f)) Txt(g, "Las vueltas aparecen aquí.", f, Sub, list, StringAlignment.Center);
                return;
            }
            // mas rapida en verde y mas lenta en rojo (como en Mac)
            var splits = new List<TimeSpan>();
            for (int i = 0; i < n; i++) splits.Add(ClockEngine.Laps[i] - (i + 1 < n ? ClockEngine.Laps[i + 1] : TimeSpan.Zero));
            int fast = -1, slow = -1;
            if (n >= 2) { fast = 0; slow = 0; for (int i = 1; i < n; i++) { if (splits[i] < splits[fast]) fast = i; if (splits[i] > splits[slow]) slow = i; } }
            var st = g.Save(); g.SetClip(listArea);
            float y = listArea.Y - scroll;
            using (var f = F(13.5f))
            {
                Action<string, TimeSpan, Color> row = (name, t, col) =>
                {
                    var rr = new RectangleF(listArea.X + 16 * s, y, listArea.Width - 32 * s, rowH); y += rowH;
                    Txt(g, name, f, col, rr);
                    Txt(g, Hms(t) + "," + (t.Milliseconds / 10).ToString("00"), f, col, rr, StringAlignment.Far);
                    using (var pen = new Pen(CardBorder)) g.DrawLine(pen, rr.X, rr.Bottom, rr.Right, rr.Bottom);
                };
                if (running) row("Vuelta " + (n + 1), el - (n > 0 ? ClockEngine.Laps[0] : TimeSpan.Zero), Fg);
                for (int i = 0; i < n; i++) row("Vuelta " + (n - i), splits[i], i == fast ? Green : i == slow ? Red : Fg);
            }
            g.Restore(st);
        }

        // ---------- temporizador ----------
        void DrawTimer(Graphics g, RectangleF r)
        {
            var c = new PointF(r.X + r.Width / 2, r.Y + (r.Height - 60 * s) / 2);
            float R = Math.Min(r.Width, r.Height - 80 * s) * 0.36f;
            float bw = 120 * s, by = r.Bottom - 40 * s, cx = r.X + r.Width / 2;
            if (ClockEngine.TimerActive)
            {
                var left = ClockEngine.TimerLeft;
                Ring(g, c, R, 10 * s, (float)(left.TotalSeconds / Math.Max(1, ClockEngine.TimerTotal.TotalSeconds)), Color.FromArgb(255, 149, 0));
                using (var f = F(Math.Max(24f, R / s * 0.42f))) Txt(g, Hms(left), f, Fg, new RectangleF(c.X - R, c.Y - R, R * 2, R * 2), StringAlignment.Center);
                using (var f = F(12.5f)) Txt(g, "Suena a las " + (DateTime.Now + left).ToString("HH:mm"), f, Sub, new RectangleF(c.X - R, c.Y + R * 0.35f, R * 2, 20 * s), StringAlignment.Center);
                Button(g, new RectangleF(cx - bw - 8 * s, by, bw, 34 * s), "Cancelar", ClockEngine.TimerCancel);
                Button(g, new RectangleF(cx + 8 * s, by, bw, 34 * s), ClockEngine.TimerRunning ? "Pausar" : "Reanudar",
                    () => { if (ClockEngine.TimerRunning) ClockEngine.TimerPause(); else ClockEngine.TimerStart(); }, true, ClockEngine.TimerRunning ? Color.FromArgb(255, 149, 0) : Green);
                return;
            }
            // elegir el tiempo
            var tt = ClockEngine.TimerTotal;
            var card = new RectangleF(cx - 200 * s, r.Y + Math.Max(6 * s, (by - r.Y - 290 * s) / 2), 400 * s, 160 * s);
            Card(g, card);
            float colW = (card.Width - 32 * s) / 3;
            string[] names = { "horas", "min", "seg" };
            int[] vals = { (int)tt.TotalHours, tt.Minutes, tt.Seconds };
            int[] steps = { 1, 1, 5 };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var col = new RectangleF(card.X + 16 * s + i * colW, card.Y + 12 * s, colW, card.Height - 24 * s);
                float ab = 26 * s;
                IconButton(g, new RectangleF(col.X + (col.Width - ab) / 2, col.Y, ab, ab), "", () => AdjustTimer(idx, steps[idx]));
                IconButton(g, new RectangleF(col.X + (col.Width - ab) / 2, col.Bottom - ab, ab, ab), "", () => AdjustTimer(idx, -steps[idx]));
                using (var f = F(40f)) Txt(g, vals[i].ToString("00"), f, Fg, new RectangleF(col.X, col.Y + ab, col.Width, col.Height - ab * 2 - 14 * s), StringAlignment.Center);
                using (var f = F(11.5f)) Txt(g, names[i], f, Sub, new RectangleF(col.X, col.Bottom - ab - 18 * s, col.Width, 16 * s), StringAlignment.Center);
            }
            // atajos
            int[] quick = { 1, 3, 5, 10, 15, 30, 60 };
            float qw = 64 * s, qg = 8 * s, total = quick.Length * qw + (quick.Length - 1) * qg, qx = cx - total / 2, qy = card.Bottom + 18 * s;
            foreach (var q in quick)
            {
                int m = q;
                Button(g, new RectangleF(qx, qy, qw, 30 * s), m == 60 ? "1 h" : m + " min", () => { ClockEngine.TimerTotal = TimeSpan.FromMinutes(m); ClockEngine.timerLeft = ClockEngine.TimerTotal; });
                qx += qw + qg;
            }
            using (var f = F(12.5f)) Txt(g, "Suena aunque cierres esta ventana.", f, Sub, new RectangleF(r.X, qy + 44 * s, r.Width, 20 * s), StringAlignment.Center);
            Button(g, new RectangleF(cx - bw / 2, by, bw, 34 * s), "Iniciar", ClockEngine.TimerStart, true, Green, tt > TimeSpan.Zero);
        }

        static void AdjustTimer(int part, int d)
        {
            var t = ClockEngine.TimerTotal;
            int h = (int)t.TotalHours, m = t.Minutes, sec = t.Seconds;
            if (part == 0) h = Math.Max(0, Math.Min(23, h + d));
            else if (part == 1) m = (m + d + 60) % 60;
            else sec = (sec + d + 60) % 60;
            ClockEngine.TimerTotal = new TimeSpan(h, m, sec);
            ClockEngine.timerLeft = ClockEngine.TimerTotal;
        }
    }
}
