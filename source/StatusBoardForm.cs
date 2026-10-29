using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    internal sealed class StatusBoardForm : Form
    {
        private readonly PetForm pet;
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer followTimer = new System.Windows.Forms.Timer();
        private readonly List<AppGroup> apps = new List<AppGroup>();
        private readonly List<Row> hitRows = new List<Row>();
        private Point target;
        private bool targetReady, loading, allowClose;
        private int offset;
        private HardwareReport hardware;

        internal event EventHandler HideRequested;

        internal StatusBoardForm(PetForm pet)
        {
            this.pet = pet;
            Text = "小deepseek · 我是大肥鱼";
            AccessibleName = "小deepseek我是大肥鱼";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = pet.ShowInTaskbar;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(252, 254, 255);
            ResizeToPet();
            pet.LocationChanged += PetChanged;
            pet.SizeChanged += PetChanged;
            pet.VisibleChanged += PetChanged;
            refreshTimer.Interval = 3000;
            refreshTimer.Tick += delegate { LoadSnapshot(); };
            followTimer.Interval = 16;
            followTimer.Tick += delegate { FollowTick(); };
            FormClosing += BoardClosing;
            MouseWheel += Wheel;
        }

        internal void ShowBoard()
        {
            MakeRoomOnRight();
            SetTarget(true);
            if (!Visible) Show();
            BringToFront();
            followTimer.Start(); refreshTimer.Start(); LoadSnapshot();
        }

        internal void Shutdown() { allowClose = true; refreshTimer.Stop(); followTimer.Stop(); Close(); }

        private float U { get { return Math.Max(.55f, Math.Min(Width / 264f, Height / 348f)); } }

        private void PetChanged(object sender, EventArgs e) { if (!IsDisposed) SetTarget(false); }

        private void ResizeToPet()
        {
            if (pet == null || pet.IsDisposed || pet.Width < 80 || pet.Height < 80) return;
            if (Size != pet.Size) Size = pet.Size;
            using (GraphicsPath cloud = CloudPath()) Region = new Region(cloud);
            Invalidate();
        }

        private GraphicsPath CloudPath()
        {
            float u = U;
            // The cloud lobes overlap.  Winding fill keeps them as one solid
            // shape; Alternate fill can punch holes through the top header.
            GraphicsPath p = new GraphicsPath(FillMode.Winding);
            Round(p, new RectangleF(7 * u, 25 * u, Width - 14 * u, Height - 33 * u), 27 * u);
            p.AddEllipse(17 * u, 10 * u, 56 * u, 54 * u);
            p.AddEllipse(55 * u, 4 * u, 78 * u, 64 * u);
            p.AddEllipse(Width - 129 * u, 8 * u, 76 * u, 60 * u);
            p.AddEllipse(Width - 65 * u, 22 * u, 52 * u, 48 * u);
            return p;
        }

        private static void Round(GraphicsPath p, RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        }

        private void SetTarget(bool direct)
        {
            if (pet == null || pet.IsDisposed || !pet.Visible) { if (Visible) Hide(); return; }
            ResizeToPet();
            Rectangle work = Screen.FromRectangle(pet.Bounds).WorkingArea;
            int gap = Math.Max(6, (int)(8 * U)), left = pet.Right + gap;
            if (left + Width > work.Right) left = pet.Left - Width - gap;
            left = Math.Max(work.Left, Math.Min(left, work.Right - Width));
            target = new Point(left, Math.Max(work.Top, Math.Min(pet.Top + (pet.Height - Height) / 2, work.Bottom - Height)));
            targetReady = true;
            if (direct) Location = target;
            if (TopMost != pet.TopMost) TopMost = pet.TopMost;
        }

        private void FollowTick()
        {
            if (pet == null || pet.IsDisposed || !pet.Visible) { if (Visible) Hide(); return; }
            if (!targetReady) SetTarget(false);
            int dx = target.X - Left, dy = target.Y - Top;
            if (dx == 0 && dy == 0) return;
            int sx = Math.Abs(dx) < 2 ? dx : (int)Math.Round(dx * .42d);
            int sy = Math.Abs(dy) < 2 ? dy : (int)Math.Round(dy * .42d);
            Location = new Point(Left + (sx == 0 ? Math.Sign(dx) : sx), Top + (sy == 0 ? Math.Sign(dy) : sy));
            if (TopMost != pet.TopMost) TopMost = pet.TopMost;
        }

        private void MakeRoomOnRight()
        {
            if (pet == null || pet.IsDisposed || !pet.Visible) return;
            ResizeToPet();
            Rectangle work = Screen.FromRectangle(pet.Bounds).WorkingArea;
            int gap = Math.Max(6, (int)(8 * U));
            if (pet.Right + gap + Width <= work.Right) return;
            int x = Math.Max(work.Left, work.Right - pet.Width - gap - Width);
            if (x + pet.Width + gap + Width <= work.Right) pet.Location = new Point(x, Math.Max(work.Top, Math.Min(pet.Top, work.Bottom - pet.Height)));
        }

        private void LoadSnapshot()
        {
            if (loading || IsDisposed || !IsHandleCreated) return;
            loading = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                HardwareReport report = StatusReporter.GetHardware();
                List<AppGroup> snapshot = GetGroups();
                try { BeginInvoke((MethodInvoker)delegate { hardware = report; apps.Clear(); apps.AddRange(snapshot); offset = Math.Max(0, Math.Min(offset, Math.Max(0, apps.Count - 1))); loading = false; Invalidate(); }); }
                catch (InvalidOperationException) { }
            });
        }

        private static List<AppGroup> GetGroups()
        {
            Dictionary<string, AppGroup> map = new Dictionary<string, AppGroup>(StringComparer.OrdinalIgnoreCase);
            int own = Process.GetCurrentProcess().Id, session = Process.GetCurrentProcess().SessionId;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == own || p.SessionId != session) continue;
                    AppGroup app; string name = p.ProcessName;
                    if (!map.TryGetValue(name, out app)) { app = new AppGroup { Name = name, CanClose = CanClose(name) }; map.Add(name, app); }
                    app.Ids.Add(p.Id); app.Bytes += p.WorkingSet64;
                }
                catch { }
                finally { p.Dispose(); }
            }
            List<AppGroup> result = new List<AppGroup>(map.Values);
            result.Sort(delegate(AppGroup a, AppGroup b) { return b.Bytes.CompareTo(a.Bytes); });
            return result;
        }

        private static bool CanClose(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            string[] protectedNames = { "idle", "system", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "dwm", "fontdrvhost", "sihost", "explorer", "taskmgr", "spoolsv" };
            foreach (string blocked in protectedNames) if (n == blocked) return false;
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float u = U; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath cloud = CloudPath()) using (SolidBrush white = new SolidBrush(Color.FromArgb(253, 254, 255))) using (Pen edge = new Pen(Color.FromArgb(174, 207, 239), Math.Max(1, 1.3f * u)))
            { e.Graphics.FillPath(white, cloud); e.Graphics.DrawPath(edge, cloud); }
            PaintHeaderClouds(e.Graphics, u);
            Decorate(e.Graphics, u); Header(e.Graphics, u); Loads(e.Graphics, u); Apps(e.Graphics, u);
        }

        private void PaintHeaderClouds(Graphics g, float u)
        {
            using (GraphicsPath puffs = new GraphicsPath(FillMode.Winding))
            {
                puffs.AddRectangle(new RectangleF(16 * u, 30 * u, Width - 32 * u, 42 * u));
                puffs.AddEllipse(10 * u, 18 * u, 72 * u, 52 * u);
                puffs.AddEllipse(51 * u, 7 * u, 91 * u, 65 * u);
                puffs.AddEllipse(Width - 151 * u, 10 * u, 93 * u, 62 * u);
                puffs.AddEllipse(Width - 78 * u, 21 * u, 67 * u, 50 * u);
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(0, 7 * u, Width, 67 * u), Color.FromArgb(210, 238, 250), Color.FromArgb(247, 232, 246), LinearGradientMode.Horizontal))
                using (SolidBrush shine = new SolidBrush(Color.FromArgb(100, 255, 255, 255)))
                using (Pen outline = new Pen(Color.FromArgb(165, 215, 239), Math.Max(1, 1.1f * u)))
                {
                    g.FillPath(fill, puffs); g.DrawPath(outline, puffs);
                    g.FillEllipse(shine, 28 * u, 15 * u, 44 * u, 20 * u);
                    g.FillEllipse(shine, Width - 116 * u, 15 * u, 53 * u, 19 * u);
                }
            }
        }

        private static void Decorate(Graphics g, float u)
        {
            using (SolidBrush blue = new SolidBrush(Color.FromArgb(136, 189, 232)))
            using (SolidBrush pink = new SolidBrush(Color.FromArgb(246, 178, 201)))
            {
                g.FillEllipse(blue, 13 * u, 77 * u, 4 * u, 4 * u);
                g.FillEllipse(pink, 13 * u, 85 * u, 3 * u, 3 * u);
                g.FillEllipse(blue, 18 * u, 91 * u, 2.5f * u, 2.5f * u);
            }
        }

        private void Header(Graphics g, float u)
        {
            using (Font title = new Font("Microsoft YaHei UI", Math.Max(20f, 21.5f * u), FontStyle.Bold, GraphicsUnit.Pixel)) using (Font sub = new Font("Microsoft YaHei UI", Math.Max(11.5f, 11.8f * u), GraphicsUnit.Pixel)) using (SolidBrush ink = new SolidBrush(Color.FromArgb(60, 95, 145))) using (SolidBrush soft = new SolidBrush(Color.FromArgb(97, 128, 165))) using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center })
            { DrawWideTitle(g, "我是大肥鱼", title, ink, u); g.DrawString("小deepseek 在旁边守着", sub, soft, new RectangleF(18 * u, 48 * u, Width - 36 * u, 14 * u), center); }
        }

        private void DrawWideTitle(Graphics g, string text, Font font, Brush brush, float u)
        {
            float first = 29 * u, last = Width - 29 * u;
            float step = text.Length > 1 ? (last - first) / (text.Length - 1) : 0;
            for (int i = 0; i < text.Length; i++)
            {
                string character = text.Substring(i, 1);
                SizeF size = g.MeasureString(character, font);
                g.DrawString(character, font, brush, new PointF(first + step * i - size.Width / 2f, 18 * u));
            }
        }

        private void Loads(Graphics g, float u)
        {
            float y = 72 * u;
            LoadBar(g, "CPU", hardware == null ? null : hardware.CpuPercent, hardware == null || !hardware.CpuPercent.HasValue ? "--" : hardware.CpuPercent.Value + "%", Color.FromArgb(107, 166, 239), y, u);
            LoadBar(g, "内存", hardware == null ? null : hardware.MemoryPercent, hardware == null || !hardware.MemoryPercent.HasValue ? "--" : hardware.MemoryPercent.Value + "%", Color.FromArgb(241, 145, 183), y + 28 * u, u);
            int? temperature = hardware != null && hardware.GpuTemperature.HasValue ? Math.Min(100, hardware.GpuTemperature.Value) : (int?)null;
            LoadBar(g, "显卡", temperature, hardware == null || !hardware.GpuTemperature.HasValue ? "--" : hardware.GpuTemperature.Value + "°", Color.FromArgb(129, 196, 159), y + 56 * u, u);
        }

        private void LoadBar(Graphics g, string label, int? value, string detail, Color color, float y, float u)
        {
            float x = 20 * u, width = Width - 40 * u, by = y + 16 * u;
            using (Font f = new Font("Microsoft YaHei UI", Math.Max(14.5f, 14.8f * u), FontStyle.Bold, GraphicsUnit.Pixel)) using (SolidBrush ink = new SolidBrush(Color.FromArgb(78, 105, 141))) using (SolidBrush faint = new SolidBrush(Color.FromArgb(228, 239, 249))) using (SolidBrush fill = new SolidBrush(color)) using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
            { g.DrawString(label, f, ink, new PointF(x, y)); g.DrawString(detail, f, ink, new RectangleF(x, y, width, 14 * u), right); RectangleF bar = new RectangleF(x, by, width, Math.Max(5, 7 * u)); g.FillRectangle(faint, bar); if (value.HasValue) g.FillRectangle(fill, new RectangleF(x, by, width * Math.Max(0, Math.Min(100, value.Value)) / 100f, bar.Height)); }
        }

        private void Apps(Graphics g, float u)
        {
            hitRows.Clear(); float hy = 160 * u;
            using (Pen p = new Pen(Color.FromArgb(218, 232, 245))) g.DrawLine(p, 20 * u, hy - 7 * u, Width - 20 * u, hy - 7 * u);
            using (Font title = new Font("Microsoft YaHei UI", Math.Max(14.5f, 14.8f * u), FontStyle.Bold, GraphicsUnit.Pixel)) using (Font hint = new Font("Microsoft YaHei UI", Math.Max(10.5f, 10.8f * u), GraphicsUnit.Pixel)) using (SolidBrush ink = new SolidBrush(Color.FromArgb(69, 98, 136))) using (SolidBrush soft = new SolidBrush(Color.FromArgb(130, 153, 180)))
            { g.DrawString("后台应用", title, ink, new PointF(20 * u, hy)); g.DrawString(apps.Count == 0 ? "读取中…" : "已合并 " + apps.Count + " 组 · 按内存", hint, soft, new PointF(20 * u, hy + 16 * u)); }
            float y = hy + 37 * u, h = Math.Max(50, 54 * u); int rows = Math.Max(1, (int)((Height - y - 22 * u) / h));
            if (apps.Count == 0) { using (Font f = new Font("Microsoft YaHei UI", Math.Max(7.4f, 7.8f * u), GraphicsUnit.Pixel)) using (SolidBrush s = new SolidBrush(Color.FromArgb(145, 162, 184))) g.DrawString("正在整理后台小伙伴…", f, s, new PointF(24 * u, y + 9 * u)); return; }
            offset = Math.Max(0, Math.Min(offset, Math.Max(0, apps.Count - rows)));
            for (int i = 0; i < rows && offset + i < apps.Count; i++) DrawRow(g, apps[offset + i], new Rectangle((int)(18 * u), (int)(y + i * h), Width - (int)(36 * u), (int)(h - 3 * u)), u);
            if (apps.Count > rows) { string page = (offset + 1) + "–" + Math.Min(apps.Count, offset + rows) + "/" + apps.Count + " · 滚轮查看"; using (Font f = new Font("Microsoft YaHei UI", Math.Max(6.2f, 6.6f * u), GraphicsUnit.Pixel)) using (SolidBrush s = new SolidBrush(Color.FromArgb(137, 158, 183))) using (StringFormat c = new StringFormat { Alignment = StringAlignment.Center }) g.DrawString(page, f, s, new RectangleF(16 * u, Height - 22 * u, Width - 32 * u, 12 * u), c); }
        }

        private void DrawRow(Graphics g, AppGroup app, Rectangle row, float u)
        {
            using (GraphicsPath card = new GraphicsPath()) using (SolidBrush f = new SolidBrush(Color.FromArgb(246, 250, 255))) using (Pen p = new Pen(Color.FromArgb(211, 229, 245))) { Round(card, row, Math.Max(7, 8 * u)); g.FillPath(f, card); g.DrawPath(p, card); }
            int bw = Math.Max(52, (int)(58 * u)); Rectangle button = new Rectangle(row.Right - bw - (int)(5 * u), row.Top + (int)(4 * u), bw, row.Height - (int)(8 * u));
            Rectangle name = new Rectangle(row.Left + (int)(8 * u), row.Top + (int)(4 * u), button.Left - row.Left - (int)(13 * u), (int)(17 * u));
            using (Font nf = new Font("Microsoft YaHei UI", Math.Max(13.8f, 14.2f * u), FontStyle.Bold, GraphicsUnit.Pixel)) using (Font df = new Font("Microsoft YaHei UI", Math.Max(10.5f, 10.8f * u), GraphicsUnit.Pixel))
            { TextRenderer.DrawText(g, app.Name, nf, name, Color.FromArgb(57, 86, 126), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding); TextRenderer.DrawText(g, Bytes(app.Bytes) + (app.Ids.Count > 1 ? "  ×" + app.Ids.Count : ""), df, new Rectangle(name.Left, row.Top + (int)(25 * u), name.Width, (int)(15 * u)), Color.FromArgb(111, 138, 171), TextFormatFlags.NoPadding); }
            using (GraphicsPath pill = new GraphicsPath()) using (SolidBrush f = new SolidBrush(app.CanClose ? Color.FromArgb(255, 231, 239) : Color.FromArgb(239, 244, 249))) using (Pen p = new Pen(app.CanClose ? Color.FromArgb(245, 164, 190) : Color.FromArgb(211, 222, 234))) using (Font font = new Font("Microsoft YaHei UI", Math.Max(11f, 11.4f * u), FontStyle.Bold, GraphicsUnit.Pixel)) using (SolidBrush b = new SolidBrush(app.CanClose ? Color.FromArgb(195, 73, 112) : Color.FromArgb(149, 164, 181))) using (StringFormat c = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            { Round(pill, button, button.Height / 2f); g.FillPath(f, pill); g.DrawPath(p, pill); g.DrawString(app.CanClose ? "关闭" : "保护", font, b, button, c); }
            if (app.CanClose) hitRows.Add(new Row { Box = button, App = app });
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (Row row in hitRows) if (row.Box.Contains(e.Location)) { CloseApp(row.App); return; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool actionable = false;
            foreach (Row row in hitRows) if (row.Box.Contains(e.Location)) { actionable = true; break; }
            Cursor = actionable ? Cursors.Hand : Cursors.Default;
        }

        private void Wheel(object sender, MouseEventArgs e) { if (apps.Count > 0) { offset = Math.Max(0, Math.Min(apps.Count - 1, offset + (e.Delta < 0 ? 1 : -1))); Invalidate(); } }

        private void CloseApp(AppGroup app)
        {
            string count = app.Ids.Count > 1 ? "的 " + app.Ids.Count + " 个进程" : "";
            if (MessageBox.Show(this, "要一键关闭 “" + app.Name + "”" + count + " 吗？\n未保存的内容可能会丢失；无窗口的后台进程也会被结束。", "小deepseek · 关闭后台应用", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (int id in app.Ids) try { using (Process p = Process.GetProcessById(id)) { if (!p.HasExited) p.CloseMainWindow(); } } catch { }
                // Give normal applications a moment to save and exit.  The one
                // confirmation above also covers background-only instances.
                Thread.Sleep(900);
                foreach (int id in app.Ids) try { using (Process p = Process.GetProcessById(id)) { if (!p.HasExited) p.Kill(); } } catch { }
                try { BeginInvoke((MethodInvoker)delegate { LoadSnapshot(); }); } catch (InvalidOperationException) { }
            });
        }

        private static string Bytes(long b) { return b >= 1024L * 1024 * 1024 ? (b / 1024d / 1024d / 1024d).ToString("0.0") + " GB" : Math.Max(0, b / 1024 / 1024) + " MB"; }
        private void HideBoard() { refreshTimer.Stop(); followTimer.Stop(); Hide(); if (HideRequested != null) HideRequested(this, EventArgs.Empty); }
        private void BoardClosing(object sender, FormClosingEventArgs e) { if (allowClose) return; e.Cancel = true; HideBoard(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { refreshTimer.Dispose(); followTimer.Dispose(); if (pet != null) { pet.LocationChanged -= PetChanged; pet.SizeChanged -= PetChanged; pet.VisibleChanged -= PetChanged; } }
            base.Dispose(disposing);
        }

        private sealed class AppGroup { internal string Name; internal long Bytes; internal bool CanClose; internal readonly List<int> Ids = new List<int>(); }
        private sealed class Row { internal Rectangle Box; internal AppGroup App; }
    }
}
