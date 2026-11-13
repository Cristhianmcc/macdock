using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacDock
{
    // Ventana "Concentración": el temporizador grande, los tiempos que quieras y tu lista de tareas.
    // Izquierda: reloj y ajustes. Derecha: tareas (la marcada con el punto es en la que estas trabajando;
    // cada concentracion terminada le suma un pomodoro).
    class FocusWindow : MacWindow
    {
        static FocusWindow current;
        readonly Timer tick = new Timer { Interval = 1000 };
        readonly TextBox box;
        RectangleF tasksArea; float tasksContentH;

        public static void Open()
        {
            try
            {
                if (current != null && !current.IsDisposed)
                {
                    if (current.WindowState == FormWindowState.Minimized) current.WindowState = FormWindowState.Normal;
                    current.Activate(); return;
                }
                current = new FocusWindow();
                current.FormClosed += (o, e) => { current = null; };
                current.Show();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        FocusWindow() : base("Concentración", 780, 500)
        {
            box = MakeBox("Escribe una tarea y presiona Enter");
            box.KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Add(); }
            };
            tick.Tick += (o, e) => Invalidate();
            tick.Start();
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        void Add() { FocusTimer.AddTask(box.Text); box.Text = ""; Invalidate(); }

        protected override void OnFormClosed(FormClosedEventArgs e) { tick.Stop(); tick.Dispose(); base.OnFormClosed(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!tasksArea.Contains(e.Location)) return;
            int max = (int)Math.Max(0, tasksContentH - tasksArea.Height);
            scroll = Math.Max(0, Math.Min(max, scroll - Math.Sign(e.Delta) * (int)(40 * s)));
            Invalidate();
        }

        static string Mmss(TimeSpan t) { return ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00"); }

        protected override void Draw(Graphics g)
        {
            g.Clear(Bg);
            float pad = 16 * s, gap = 12 * s;
            float W = ClientSize.Width, H = ClientSize.Height;
            float leftW = Math.Min(360 * s, (W - pad * 2 - gap) * 0.48f);
            var colL = new RectangleF(pad, pad, leftW, H - pad * 2);
            var colR = new RectangleF(pad + leftW + gap, pad, W - pad * 2 - gap - leftW, H - pad * 2);

            // ---------- reloj ----------
            float timerH = Math.Max(200 * s, colL.Height - 222 * s - gap);
            var tc = new RectangleF(colL.X, colL.Y, colL.Width, timerH);
            Card(g, tc);
            bool brk = FocusTimer.Current != FocusTimer.Phase.Focus;
            Color accent = brk ? Green : Tomato;
            float ip = 16 * s;
            using (var b = new SolidBrush(accent)) g.FillEllipse(b, tc.X + ip, tc.Y + ip + 5 * s, 9 * s, 9 * s);
            using (var f = F(14f, true)) Txt(g, FocusTimer.PhaseName, f, Fg, new RectangleF(tc.X + ip + 15 * s, tc.Y + ip, tc.Width - ip * 2, 20 * s));
            using (var f = F(12f)) Txt(g, "Hoy: " + FocusTimer.Today + (FocusTimer.Today == 1 ? " pomodoro" : " pomodoros"), f, Sub, new RectangleF(tc.X, tc.Y + ip, tc.Width - ip, 20 * s), StringAlignment.Far);

            float btnY = tc.Bottom - ip - 30 * s;
            float top = tc.Y + ip + 26 * s, bottom = btnY - 44 * s;
            float R = Math.Max(30 * s, Math.Min(tc.Width * 0.3f, (bottom - top) / 2f - 6 * s));
            var c = new PointF(tc.X + tc.Width / 2, top + (bottom - top) / 2f);
            var left = FocusTimer.Left; var total = FocusTimer.Total;
            Ring(g, c, R, 9 * s, total.TotalSeconds > 0 ? (float)(left.TotalSeconds / total.TotalSeconds) : 0, accent);
            using (var f = F(Math.Max(18f, R / s * 0.5f), true)) Txt(g, Mmss(left), f, Fg, new RectangleF(c.X - R, c.Y - R, R * 2, R * 2), StringAlignment.Center);

            // puntos de ronda y tarea actual
            float dot = 7 * s, dg = 6 * s, dw = FocusTimer.Rounds * dot + (FocusTimer.Rounds - 1) * dg, dx = c.X - dw / 2, dy = c.Y + R + 12 * s;
            for (int i = 1; i <= FocusTimer.Rounds; i++)
            {
                bool full = i < FocusTimer.Round || (i == FocusTimer.Round && brk);
                var dr = new RectangleF(dx + (i - 1) * (dot + dg), dy, dot, dot);
                if (full || i == FocusTimer.Round) using (var b = new SolidBrush(full ? Tomato : Color.FromArgb(120, Tomato))) g.FillEllipse(b, dr);
                else using (var pen = new Pen(Sub, 1.2f * s)) g.DrawEllipse(pen, dr);
            }
            var ct = FocusTimer.CurrentTask;
            using (var f = F(12f)) Txt(g, ct != null ? ct.Text : "Sin tarea elegida", f, ct != null ? Fg : Sub, new RectangleF(tc.X + ip, dy + 12 * s, tc.Width - ip * 2, 18 * s), StringAlignment.Center);

            float bw = (tc.Width - ip * 2 - 16 * s) / 3f;
            Button(g, new RectangleF(tc.X + ip, btnY, bw, 30 * s), FocusTimer.Running ? "Pausar" : "Iniciar", FocusTimer.Toggle, true, accent);
            Button(g, new RectangleF(tc.X + ip + bw + 8 * s, btnY, bw, 30 * s), "Saltar", FocusTimer.Skip);
            Button(g, new RectangleF(tc.X + ip + (bw + 8 * s) * 2, btnY, bw, 30 * s), "Reiniciar", FocusTimer.Reset);

            // ---------- tiempos ----------
            var sc = new RectangleF(colL.X, tc.Bottom + gap, colL.Width, colL.Bottom - tc.Bottom - gap);
            Card(g, sc);
            float rowH = 36 * s, y = sc.Y + 8 * s;
            var row = new Func<RectangleF>(() => { var r = new RectangleF(sc.X + ip, y, sc.Width - ip * 2, rowH); y += rowH; return r; });
            Stepper(g, row(), "Concentración", FocusTimer.FocusMin + " min",
                () => FocusTimer.SetDurations(Step(FocusTimer.FocusMin, -5, 5, 90), FocusTimer.BreakMin, FocusTimer.LongMin),
                () => FocusTimer.SetDurations(Step(FocusTimer.FocusMin, 5, 5, 90), FocusTimer.BreakMin, FocusTimer.LongMin));
            Stepper(g, row(), "Descanso", FocusTimer.BreakMin + " min",
                () => FocusTimer.SetDurations(FocusTimer.FocusMin, Step(FocusTimer.BreakMin, -1, 1, 30), FocusTimer.LongMin),
                () => FocusTimer.SetDurations(FocusTimer.FocusMin, Step(FocusTimer.BreakMin, 1, 1, 30), FocusTimer.LongMin));
            Stepper(g, row(), "Descanso largo", FocusTimer.LongMin + " min",
                () => FocusTimer.SetDurations(FocusTimer.FocusMin, FocusTimer.BreakMin, Step(FocusTimer.LongMin, -5, 5, 60)),
                () => FocusTimer.SetDurations(FocusTimer.FocusMin, FocusTimer.BreakMin, Step(FocusTimer.LongMin, 5, 5, 60)));
            Stepper(g, row(), "Rondas antes del largo", FocusTimer.Rounds.ToString(),
                () => FocusTimer.SetRounds(FocusTimer.Rounds - 1), () => FocusTimer.SetRounds(FocusTimer.Rounds + 1));
            var cr = row();
            Check(g, new RectangleF(cr.X + 2 * s, cr.Y + (rowH - 18 * s) / 2, 18 * s, 18 * s), FocusTimer.SilenceWhileFocus, () => FocusTimer.SetSilence(!FocusTimer.SilenceWhileFocus));
            using (var f = F(12.5f)) Txt(g, "Activar No molestar al concentrarme", f, Fg, new RectangleF(cr.X + 30 * s, cr.Y, cr.Width - 30 * s, rowH));
            Hit(new RectangleF(cr.X + 30 * s, cr.Y, cr.Width - 30 * s, rowH), () => FocusTimer.SetSilence(!FocusTimer.SilenceWhileFocus));

            DrawTasks(g, colR, ip);
        }

        static int Step(int v, int d, int min, int max) { return Math.Max(min, Math.Min(max, v + d)); }

        void DrawTasks(Graphics g, RectangleF r, float ip)
        {
            Card(g, r);
            using (var f = F(14f, true)) Txt(g, "Tareas", f, Fg, new RectangleF(r.X + ip, r.Y + ip, r.Width - ip * 2, 20 * s));
            int done = FocusTimer.Tasks.FindAll(t => t.Done).Count;
            if (done > 0)
            {
                var clr = new RectangleF(r.Right - ip - 130 * s, r.Y + ip, 130 * s, 20 * s);
                using (var f = F(12f)) Txt(g, "Borrar terminadas", f, Accent, clr, StringAlignment.Far);
                Hit(clr, () => { FocusTimer.ClearDone(); scroll = 0; });
            }

            // caja para escribir + boton
            var inR = new RectangleF(r.X + ip, r.Y + ip + 30 * s, r.Width - ip * 2 - 92 * s, 32 * s);
            using (var p = Round(inR, 8 * s)) using (var b = new SolidBrush(Soft)) g.FillPath(b, p);
            var bb = Rectangle.Round(new RectangleF(inR.X + 10 * s, inR.Y + (inR.Height - box.PreferredHeight) / 2, inR.Width - 20 * s, box.PreferredHeight));
            if (box.Bounds != bb) box.Bounds = bb;
            Button(g, new RectangleF(inR.Right + 8 * s, inR.Y + 1 * s, 84 * s, 30 * s), "Agregar", Add, true);

            // lista
            tasksArea = new RectangleF(r.X + 4 * s, inR.Bottom + 10 * s, r.Width - 8 * s, r.Bottom - inR.Bottom - 14 * s);
            float rowH = 40 * s;
            tasksContentH = FocusTimer.Tasks.Count * rowH;
            int max = (int)Math.Max(0, tasksContentH - tasksArea.Height); if (scroll > max) scroll = max;
            if (FocusTimer.Tasks.Count == 0)
            {
                using (var f = F(12.5f)) Txt(g, "Agrega en qué vas a trabajar.\nCada concentración terminada le suma un pomodoro a la tarea elegida.", f, Sub,
                    RectangleF.Inflate(tasksArea, -20 * s, 0), StringAlignment.Center);
                return;
            }
            var st = g.Save();
            g.SetClip(tasksArea);
            float y = tasksArea.Y - scroll;
            using (var f = F(13f)) using (var fs = F(11.5f, true))
                foreach (var t in FocusTimer.Tasks.ToArray())
                {
                    var task = t;
                    var row = new RectangleF(tasksArea.X + 6 * s, y, tasksArea.Width - 12 * s, rowH - 4 * s);
                    y += rowH;
                    if (row.Bottom < tasksArea.Y || row.Y > tasksArea.Bottom) continue;
                    bool cur = task == FocusTimer.CurrentTask;
                    if (cur) using (var p = Round(row, 8 * s)) using (var b = new SolidBrush(Color.FromArgb(light ? 36 : 60, Tomato))) g.FillPath(b, p);
                    var vis = RectangleF.Intersect(row, tasksArea);

                    var chk = new RectangleF(row.X + 8 * s, row.Y + (row.Height - 18 * s) / 2, 18 * s, 18 * s);
                    Check(g, chk, task.Done, () => FocusTimer.ToggleDone(task), Green);

                    var del = new RectangleF(row.Right - 30 * s, row.Y + (row.Height - 24 * s) / 2, 24 * s, 24 * s);
                    using (var fi = Icons(10f)) Txt(g, "", fi, Sub, del, StringAlignment.Center);

                    // pomodoros de la tarea: punto rojo + numero
                    float cntW = task.Count > 0 ? 40 * s : 0;
                    if (task.Count > 0)
                    {
                        var cr = new RectangleF(del.X - cntW, row.Y, cntW, row.Height);
                        using (var b = new SolidBrush(Tomato)) g.FillEllipse(b, cr.X + 4 * s, cr.Y + (cr.Height - 9 * s) / 2, 9 * s, 9 * s);
                        Txt(g, task.Count.ToString(), fs, Sub, new RectangleF(cr.X + 16 * s, cr.Y, cr.Width - 16 * s, cr.Height));
                    }
                    var tr = new RectangleF(chk.Right + 10 * s, row.Y, del.X - cntW - chk.Right - 14 * s, row.Height);
                    Txt(g, task.Text, f, task.Done ? Sub : Fg, tr);
                    if (task.Done) using (var pen = new Pen(Sub, 1f * s)) { var sz = g.MeasureString(task.Text, f); g.DrawLine(pen, tr.X, tr.Y + tr.Height / 2, tr.X + Math.Min(tr.Width, sz.Width), tr.Y + tr.Height / 2); }

                    // clic en la fila: elegirla como tarea actual (los botones de la fila se registran despues y ganan)
                    if (!task.Done) Hit(new RectangleF(tr.X, vis.Y, tr.Width + cntW, vis.Height), () => FocusTimer.SelectTask(task));
                    if (del.Y >= tasksArea.Y && del.Bottom <= tasksArea.Bottom) Hit(del, () => FocusTimer.RemoveTask(task));
                }
            g.Restore(st);
        }
    }
}
