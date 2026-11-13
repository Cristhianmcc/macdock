using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacDock
{
    // Ventana "Concentración" con aspecto de app de Mac.
    // Izquierda: en que fase estas (pestañas), el reloj, que sigue y los controles con su nombre.
    // Derecha: tareas (como Recordatorios) y abajo los ajustes con menus desplegables.
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

        FocusWindow() : base("Concentración", 860, 600)
        {
            box = MakeBox("Nueva tarea…");
            box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Add(); } };
            tick.Tick += (o, e) => Invalidate();
            tick.Start();
            KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Space && !box.Focused) { FocusTimer.Toggle(); Invalidate(); e.Handled = true; }   // espacio: empezar/pausar
            };
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
        static Color PhaseColor(FocusTimer.Phase p) { return p == FocusTimer.Phase.Focus ? Tomato : p == FocusTimer.Phase.Break ? Green : Color.FromArgb(48, 176, 199); }

        // en la barra de arriba, a la derecha: cuantos llevas hoy
        protected override void DrawTitleBar(Graphics g, RectangleF bar)
        {
            int n = FocusTimer.Today;
            using (var f = F(12f)) Txt(g, "Hoy: " + n + (n == 1 ? " pomodoro" : " pomodoros"), f, Sub, new RectangleF(bar.Right - 216 * s, bar.Y, 200 * s, bar.Height), StringAlignment.Far);
        }

        protected override void Draw(Graphics g)
        {
            float pad = 16 * s, gap = 12 * s;
            float W = ClientSize.Width, top = TitleH + 14 * s, H = ClientSize.Height - top - pad;
            float leftW = Math.Min(400 * s, (W - pad * 2 - gap) * 0.48f);
            var colL = new RectangleF(pad, top, leftW, H);
            var colR = new RectangleF(pad + leftW + gap, top, W - pad * 2 - gap - leftW, H);
            DrawTimer(g, colL);
            float setH = 214 * s;
            DrawTasks(g, new RectangleF(colR.X, colR.Y, colR.Width, colR.Height - setH - gap));
            DrawSettings(g, new RectangleF(colR.X, colR.Bottom - setH, colR.Width, setH));
        }

        // ---------- reloj ----------
        void DrawTimer(Graphics g, RectangleF tc)
        {
            Card(g, tc);
            float ip = 16 * s;
            var cur = FocusTimer.Current; Color accent = PhaseColor(cur);

            // en que fase estas: se ve y se puede cambiar
            var phases = new[] { FocusTimer.Phase.Focus, FocusTimer.Phase.Break, FocusTimer.Phase.LongBreak };
            Segments(g, new RectangleF(tc.X + ip, tc.Y + ip, tc.Width - ip * 2, 30 * s), new[] { "Concentración", "Descanso", "Descanso largo" },
                (int)cur, i => FocusTimer.SetPhase(phases[i]), accent);

            // controles abajo, con su nombre
            float big = 58 * s, small = 44 * s, labelH = 18 * s;
            float ctrlY = tc.Bottom - ip - labelH - big;
            float cx = tc.X + tc.Width / 2;
            var play = new RectangleF(cx - big / 2, ctrlY, big, big);
            var reset = new RectangleF(cx - big / 2 - 34 * s - small, ctrlY + (big - small) / 2, small, small);
            var skip = new RectangleF(cx + big / 2 + 34 * s, reset.Y, small, small);
            IconButton(g, play, FocusTimer.Running ? "pause" : "play", FocusTimer.Toggle, accent, Color.White);
            IconButton(g, reset, "reset", FocusTimer.Reset);
            IconButton(g, skip, "skip", FocusTimer.Skip);
            string playLabel = FocusTimer.Running ? "Pausar" : FocusTimer.Started ? "Continuar" : "Empezar";
            using (var f = F(11.5f))
            {
                Txt(g, playLabel, f, Fg, new RectangleF(play.X - 30 * s, play.Bottom + 2 * s, play.Width + 60 * s, labelH), StringAlignment.Center);
                Txt(g, "Reiniciar", f, Sub, new RectangleF(reset.X - 30 * s, play.Bottom + 2 * s, reset.Width + 60 * s, labelH), StringAlignment.Center);
                Txt(g, "Saltar", f, Sub, new RectangleF(skip.X - 30 * s, play.Bottom + 2 * s, skip.Width + 60 * s, labelH), StringAlignment.Center);
            }

            // tarea actual y que sigue
            float infoY = ctrlY - 18 * s - 64 * s;
            var ct = FocusTimer.CurrentTask;
            using (var f = F(11.5f)) Txt(g, ct != null ? "TRABAJANDO EN" : "SIN TAREA", f, Sub, new RectangleF(tc.X + ip, infoY, tc.Width - ip * 2, 16 * s), StringAlignment.Center);
            using (var f = F(14f, true)) Txt(g, ct != null ? ct.Text : "Elige una tarea a la derecha", f, ct != null ? Fg : Sub, new RectangleF(tc.X + ip, infoY + 17 * s, tc.Width - ip * 2, 22 * s), StringAlignment.Center);
            var nx = FocusTimer.Next;
            using (var f = F(12f))
            {
                string after = "Después: " + FocusTimer.NameOf(nx) + " de " + FocusTimer.MinutesOf(nx) + " min";
                float tw = g.MeasureString(after, f).Width;
                var dr = new RectangleF(cx - tw / 2 - 8 * s, infoY + 46 * s, tw + 16 * s, 20 * s);
                using (var b = new SolidBrush(PhaseColor(nx))) g.FillEllipse(b, dr.X - 4 * s, dr.Y + 6.5f * s, 7 * s, 7 * s);
                Txt(g, after, f, Sub, new RectangleF(dr.X + 8 * s, dr.Y, tw + 8 * s, dr.Height));
            }

            // reloj
            float ringTop = tc.Y + ip + 30 * s + 18 * s, ringBottom = infoY - 34 * s;
            float R = Math.Max(40 * s, Math.Min(tc.Width * 0.27f, (ringBottom - ringTop) / 2f));
            var c = new PointF(cx, ringTop + (ringBottom - ringTop) / 2f);
            var left = FocusTimer.Left; var total = FocusTimer.Total;
            Ring(g, c, R, 10 * s, total.TotalSeconds > 0 ? (float)(left.TotalSeconds / total.TotalSeconds) : 0, accent);
            using (var f = F(Math.Max(22f, R / s * 0.46f), true)) Txt(g, Mmss(left), f, Fg, new RectangleF(c.X - R, c.Y - R * 0.45f, R * 2, R * 0.7f), StringAlignment.Center);
            string state = FocusTimer.Running ? "Termina a las " + (DateTime.Now + left).ToString("HH:mm") : FocusTimer.Started ? "En pausa" : "Lista para empezar";
            using (var f = F(12f)) Txt(g, state, f, FocusTimer.Running ? Sub : accent, new RectangleF(c.X - R, c.Y + R * 0.22f, R * 2, 18 * s), StringAlignment.Center);

            // ronda: puntitos y texto
            float dot = 7 * s, dg = 6 * s; int n = FocusTimer.Rounds;
            string rt = "Ronda " + Math.Min(FocusTimer.Round, n) + " de " + n;
            float tw2; using (var f = F(12f)) tw2 = g.MeasureString(rt, f).Width;
            float dw = n * dot + (n - 1) * dg, total2 = dw + 10 * s + tw2, dx = cx - total2 / 2, dy = c.Y + R + 14 * s;
            for (int i = 1; i <= n; i++)
            {
                bool done = i < FocusTimer.Round || (i == FocusTimer.Round && cur != FocusTimer.Phase.Focus);
                var dr = new RectangleF(dx + (i - 1) * (dot + dg), dy + 5 * s, dot, dot);
                if (done) using (var b = new SolidBrush(Tomato)) g.FillEllipse(b, dr);
                else using (var pen = new Pen(i == FocusTimer.Round ? Tomato : Sub, 1.3f * s)) g.DrawEllipse(pen, dr);
            }
            using (var f = F(12f)) Txt(g, rt, f, Sub, new RectangleF(dx + dw + 10 * s, dy, tw2 + 4 * s, 17 * s));
        }

        // ---------- ajustes ----------
        void DrawSettings(Graphics g, RectangleF sc)
        {
            Card(g, sc);
            float ip = 16 * s, rowH = 38 * s, y = sc.Y + 8 * s;
            Func<RectangleF> row = () =>
            {
                var r = new RectangleF(sc.X + ip, y, sc.Width - ip * 2, rowH);
                if (y > sc.Y + 10 * s) using (var pen = new Pen(CardBorder)) g.DrawLine(pen, r.X, r.Y, r.Right, r.Y);
                y += rowH; return r;
            };
            int[] fo = { 10, 15, 20, 25, 30, 35, 40, 45, 50, 60, 75, 90 }, bo = { 3, 5, 10, 15, 20 }, lo = { 10, 15, 20, 25, 30 }, ro = { 2, 3, 4, 5, 6, 8 };
            Minutes(g, row(), "Concentración", FocusTimer.FocusMin, fo, v => FocusTimer.SetDurations(v, FocusTimer.BreakMin, FocusTimer.LongMin), " min");
            Minutes(g, row(), "Descanso", FocusTimer.BreakMin, bo, v => FocusTimer.SetDurations(FocusTimer.FocusMin, v, FocusTimer.LongMin), " min");
            Minutes(g, row(), "Descanso largo", FocusTimer.LongMin, lo, v => FocusTimer.SetDurations(FocusTimer.FocusMin, FocusTimer.BreakMin, v), " min");
            Minutes(g, row(), "Descanso largo cada", FocusTimer.Rounds, ro, FocusTimer.SetRounds, " rondas");
            var cr = row();
            using (var f = F(13f)) Txt(g, "No molestar al concentrarme", f, Fg, cr);
            Switch(g, new RectangleF(cr.Right - 40 * s, cr.Y + (rowH - 22 * s) / 2, 40 * s, 22 * s), FocusTimer.SilenceWhileFocus, () => FocusTimer.SetSilence(!FocusTimer.SilenceWhileFocus));
        }

        void Minutes(Graphics g, RectangleF r, string label, int value, int[] options, Action<int> set, string unit)
        {
            // el valor actual siempre aparece en la lista, aunque venga de una version anterior
            var list = new System.Collections.Generic.List<int>(options);
            if (!list.Contains(value)) { list.Add(value); list.Sort(); }
            var names = new string[list.Count]; int sel = 0;
            for (int i = 0; i < list.Count; i++) { names[i] = list[i] + unit; if (list[i] == value) sel = i; }
            PopupRow(g, r, label, value + unit, names, sel, i => set(list[i]));
        }

        // ---------- tareas (como Recordatorios) ----------
        void DrawTasks(Graphics g, RectangleF r)
        {
            Card(g, r);
            float ip = 16 * s;
            int pending = FocusTimer.Tasks.FindAll(t => !t.Done).Count, done = FocusTimer.Tasks.Count - pending;
            using (var f = F(15f, true)) Txt(g, "Tareas", f, Fg, new RectangleF(r.X + ip, r.Y + ip - 2 * s, 120 * s, 22 * s));
            using (var f = F(12f)) Txt(g, pending == 0 ? "" : pending + (pending == 1 ? " pendiente" : " pendientes"), f, Sub, new RectangleF(r.X + ip + 62 * s, r.Y + ip - 2 * s, 140 * s, 22 * s));
            if (done > 0)
            {
                var clr = new RectangleF(r.Right - ip - 130 * s, r.Y + ip - 2 * s, 130 * s, 22 * s);
                using (var f = F(12f)) Txt(g, "Borrar terminadas", f, Hover(clr) ? ControlPaint.Light(Accent, 0.3f) : Accent, clr, StringAlignment.Far);
                Hit(clr, () => { FocusTimer.ClearDone(); scroll = 0; });
            }
            using (var f = F(11.5f)) Txt(g, "Toca una tarea para trabajar en ella.", f, Sub, new RectangleF(r.X + ip, r.Y + ip + 20 * s, r.Width - ip * 2, 16 * s));

            // escribir una tarea
            var inR = new RectangleF(r.X + ip, r.Y + ip + 44 * s, r.Width - ip * 2 - 40 * s, 32 * s);
            using (var p = Round(inR, 8 * s)) using (var b = new SolidBrush(Soft)) g.FillPath(b, p);
            var bb = Rectangle.Round(new RectangleF(inR.X + 10 * s, inR.Y + (inR.Height - box.PreferredHeight) / 2, inR.Width - 20 * s, box.PreferredHeight));
            if (box.Bounds != bb) box.Bounds = bb;
            IconButton(g, new RectangleF(inR.Right + 8 * s, inR.Y + 1 * s, 30 * s, 30 * s), "plus", Add, Accent, Color.White);

            // lista
            tasksArea = new RectangleF(r.X + 4 * s, inR.Bottom + 8 * s, r.Width - 8 * s, r.Bottom - inR.Bottom - 12 * s);
            float rowH = 40 * s;
            tasksContentH = FocusTimer.Tasks.Count * rowH;
            int max = (int)Math.Max(0, tasksContentH - tasksArea.Height); if (scroll > max) scroll = max;
            if (FocusTimer.Tasks.Count == 0)
            {
                using (var f = F(12.5f)) Txt(g, "Sin tareas por ahora", f, Sub, tasksArea, StringAlignment.Center);
                return;
            }
            var st = g.Save();
            g.SetClip(tasksArea);
            float y = tasksArea.Y - scroll;
            using (var f = F(13f)) using (var fb = F(13f, true)) using (var fs = F(11.5f, true))
                foreach (var t in FocusTimer.Tasks.ToArray())
                {
                    var task = t;
                    var row = new RectangleF(tasksArea.X + 6 * s, y, tasksArea.Width - 12 * s, rowH - 2 * s);
                    y += rowH;
                    if (row.Bottom < tasksArea.Y || row.Y > tasksArea.Bottom) continue;
                    var vis = RectangleF.Intersect(row, tasksArea);
                    bool cur = task == FocusTimer.CurrentTask, hov = Hover(vis);
                    if (hov) using (var p = Round(row, 8 * s)) using (var b = new SolidBrush(HoverBg)) g.FillPath(b, p);

                    var chk = new RectangleF(row.X + 8 * s, row.Y + (row.Height - 18 * s) / 2, 18 * s, 18 * s);
                    Check(g, chk, task.Done, () => FocusTimer.ToggleDone(task), Tomato);

                    float right = row.Right - 6 * s;
                    if (hov)
                    {
                        var del = new RectangleF(right - 22 * s, row.Y + (row.Height - 22 * s) / 2, 22 * s, 22 * s);
                        IconButton(g, del, "x", () => FocusTimer.RemoveTask(task), Color.Transparent, Sub);
                        right = del.X - 4 * s;
                    }
                    if (cur)
                    {
                        string tag = "En curso"; float tw = g.MeasureString(tag, fs).Width + 14 * s;
                        var pr = new RectangleF(right - tw, row.Y + (row.Height - 20 * s) / 2, tw, 20 * s);
                        using (var p = Round(pr, 10 * s)) using (var b = new SolidBrush(Color.FromArgb(light ? 40 : 60, Tomato))) g.FillPath(b, p);
                        Txt(g, tag, fs, Tomato, pr, StringAlignment.Center);
                        right = pr.X - 6 * s;
                    }
                    if (task.Count > 0)
                    {
                        string cn = task.Count.ToString(); float cw = g.MeasureString(cn, fs).Width + 16 * s;
                        var cr = new RectangleF(right - cw, row.Y, cw, row.Height);
                        using (var b = new SolidBrush(Tomato)) g.FillEllipse(b, cr.X + 2 * s, cr.Y + (cr.Height - 8 * s) / 2, 8 * s, 8 * s);
                        Txt(g, cn, fs, Sub, new RectangleF(cr.X + 13 * s, cr.Y, cw, cr.Height));
                        right = cr.X - 6 * s;
                    }
                    var tr = new RectangleF(chk.Right + 12 * s, row.Y, right - chk.Right - 12 * s, row.Height);
                    Txt(g, task.Text, cur ? fb : f, task.Done ? Sub : Fg, tr);
                    if (task.Done) using (var pen = new Pen(Sub, 1f * s)) { var sz = g.MeasureString(task.Text, f); g.DrawLine(pen, tr.X, tr.Y + tr.Height / 2, tr.X + Math.Min(tr.Width, sz.Width), tr.Y + tr.Height / 2); }

                    if (!task.Done) Hit(new RectangleF(tr.X, vis.Y, tr.Width, vis.Height), () => FocusTimer.SelectTask(task));
                }
            g.Restore(st);
        }
    }
}
