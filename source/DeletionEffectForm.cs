using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    // This window only animates a copy of the desktop icon. It never handles files.
    internal sealed class DeletionEffectForm : Form
    {
        private sealed class Spark
        {
            internal float X, Y, VX, VY, Age, Life, Size, Phase;
            internal bool Star;
        }

        private readonly Bitmap icon;
        private readonly Point target;
        private readonly float pixelScale;
        private readonly Random random = new Random();
        private readonly List<Spark> sparks = new List<Spark>(96);
        private Point mouth;
        private Bitmap frame;
        private float age, eatingAge, emission;
        private bool eating, mouthBurst;
        private const float EatDuration = 1.12f;
        private const float FinishTime = 2.05f;

        internal DeletionEffectForm(Bitmap icon, Point center, float scale)
        {
            this.icon = icon == null ? null : (Bitmap)icon.Clone();
            target = center;
            mouth = center;
            pixelScale = float.IsNaN(scale) || float.IsInfinity(scale) ? 1f : Math.Max(0.5f, Math.Min(4f, scale));
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            RecalculateOverlayBounds();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Render();
        }

        internal void SetMouth(Point center)
        {
            mouth = center;
            if (eating) RecalculateOverlayBounds();
        }

        internal void BeginEating()
        {
            if (eating || IsDisposed) return;
            eating = true;
            eatingAge = 0;
            RecalculateOverlayBounds();
            Burst(target.X, target.Y, 34, false);
        }

        // Returns true once all visual work is finished; the owner disposes the window.
        internal bool Tick(float dt)
        {
            if (IsDisposed) return true;
            if (float.IsNaN(dt) || float.IsInfinity(dt)) dt = 0;
            dt = Math.Max(0, Math.Min(0.1f, dt));
            age += dt;
            if (!eating) return false;
            eatingAge += dt;
            if (eatingAge < 0.95f)
            {
                emission += dt * 24;
                while (emission >= 1)
                {
                    emission -= 1;
                    PointF center = IconCenter();
                    AddSpark(center.X, center.Y, false);
                }
            }
            if (!mouthBurst && eatingAge >= 0.9f)
            {
                mouthBurst = true;
                Burst(mouth.X, mouth.Y, 30, true);
            }
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                Spark spark = sparks[i];
                spark.Age += dt;
                if (spark.Age >= spark.Life) { sparks.RemoveAt(i); continue; }
                if (eatingAge < 0.9f)
                {
                    spark.VX += (mouth.X - spark.X) * dt * 3.8f;
                    spark.VY += (mouth.Y - spark.Y) * dt * 3.8f;
                }
                float drag = (float)Math.Exp(-2.6f * dt);
                spark.VX *= drag;
                spark.VY = spark.VY * drag - 12 * pixelScale * dt;
                spark.X += spark.VX * dt;
                spark.Y += spark.VY * dt;
            }
            return eatingAge >= FinishTime;
        }

        private void RecalculateOverlayBounds()
        {
            int margin = (int)Math.Ceiling(120 * pixelScale);
            Point other = eating ? mouth : target;
            Rectangle desired = Rectangle.FromLTRB(Math.Min(target.X, other.X) - margin,
                Math.Min(target.Y, other.Y) - margin, Math.Max(target.X, other.X) + margin,
                Math.Max(target.Y, other.Y) + margin);
            Rectangle clipped = Rectangle.Intersect(desired, SystemInformation.VirtualScreen);
            if (clipped.Width <= 0 || clipped.Height <= 0)
                clipped = new Rectangle(target.X, target.Y, 1, 1);
            if (Bounds != clipped) Bounds = clipped;
        }

        private PointF IconCenter()
        {
            float t = Math.Min(1, eatingAge / EatDuration);
            float ease = t * t * (3 - 2 * t);
            return new PointF(target.X + (mouth.X - target.X) * ease,
                target.Y + (mouth.Y - target.Y) * ease - (float)Math.Sin(t * Math.PI) * 28 * pixelScale);
        }

        private void Burst(float x, float y, int count, bool outward)
        {
            for (int i = 0; i < count; i++) AddSpark(x, y, outward);
        }

        private void AddSpark(float x, float y, bool outward)
        {
            if (sparks.Count >= 96) return;
            float angle = (float)(random.NextDouble() * Math.PI * 2);
            float velocity = (outward ? 48 : 26) + (float)random.NextDouble() * (outward ? 106 : 74);
            float spread = (outward ? 4 : 20) * pixelScale;
            sparks.Add(new Spark
            {
                X = x + (float)Math.Cos(angle) * spread,
                Y = y + (float)Math.Sin(angle) * spread,
                VX = (float)Math.Cos(angle) * velocity * pixelScale,
                VY = (float)Math.Sin(angle) * velocity * pixelScale,
                Life = (outward ? 0.52f : 0.45f) + (float)random.NextDouble() * 0.46f,
                Size = (1.4f + (float)random.NextDouble() * 2.9f) * pixelScale,
                Phase = (float)random.NextDouble() * 6.28f,
                Star = random.Next(4) == 0
            });
        }

        internal void Render()
        {
            if (IsDisposed || !Visible || Width <= 0 || Height <= 0) return;
            if (frame == null || frame.Width != Width || frame.Height != Height)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform(-Left, -Top);

                float progress = eating ? Math.Min(1, eatingAge / EatDuration) : 0;
                float ringAlpha = eating ? Math.Max(0, 1 - progress * 1.7f) : 0.7f;
                DrawOrbit(g, ringAlpha);

                if (!eating || progress < 1) DrawIcon(g, progress);
                foreach (Spark spark in sparks) DrawSpark(g, spark);

                if (mouthBurst && eatingAge < 1.5f)
                {
                    float pulse = Math.Min(1, (eatingAge - 0.9f) / 0.6f);
                    float radius = (8 + pulse * 37) * pixelScale;
                    using (Pen ring = new Pen(Color.FromArgb((int)((1 - pulse) * 155), 72, 202, 255), 2 * pixelScale))
                        g.DrawEllipse(ring, mouth.X - radius, mouth.Y - radius, radius * 2, radius * 2);
                    Glow(g, mouth.X, mouth.Y, (1 - pulse) * 10 * pixelScale, (1 - pulse) * 0.7f);
                }
            }
            Native.Present(this, frame);
        }

        private void DrawOrbit(Graphics g, float opacity)
        {
            if (opacity <= 0) return;
            float radius = (34 + (float)Math.Sin(age * 4) * 2) * pixelScale;
            using (Pen ring = new Pen(Color.FromArgb((int)(opacity * 50), 48, 163, 255), 1.2f * pixelScale))
                g.DrawEllipse(ring, target.X - radius, target.Y - radius * 0.46f, radius * 2, radius * 0.92f);
            for (int i = 0; i < 9; i++)
            {
                float angle = age * 2.3f + i * (float)(Math.PI * 2 / 9);
                float x = target.X + (float)Math.Cos(angle) * radius;
                float y = target.Y + (float)Math.Sin(angle) * radius * 0.46f;
                Glow(g, x, y, (1.8f + 0.5f * (float)Math.Sin(angle)) * pixelScale, opacity);
            }
        }

        private void DrawIcon(Graphics g, float progress)
        {
            PointF center = eating ? IconCenter() : new PointF(target.X, target.Y);
            float shrink = Math.Max(0.025f, 1 - progress * progress);
            float opacity = eating ? Math.Min(1, (1 - progress) * 3) : 1;
            float side = 48 * pixelScale;
            float width = icon == null ? side * 0.74f : side * icon.Width / Math.Max(icon.Width, icon.Height);
            float height = icon == null ? side : side * icon.Height / Math.Max(icon.Width, icon.Height);
            GraphicsState state = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(eating ? progress * progress * 430 : (float)Math.Sin(age * 3) * 2);
            g.ScaleTransform(shrink, shrink);
            Glow(g, 0, 0, 11 * pixelScale, opacity * 0.24f);
            if (icon != null)
            {
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix();
                    matrix.Matrix33 = opacity;
                    attributes.SetColorMatrix(matrix);
                    Rectangle destination = new Rectangle((int)(-width / 2), (int)(-height / 2), Math.Max(1, (int)width), Math.Max(1, (int)height));
                    g.DrawImage(icon, destination, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            else
            {
                using (SolidBrush paper = new SolidBrush(Color.FromArgb((int)(opacity * 245), 225, 245, 255)))
                using (Pen edge = new Pen(Color.FromArgb((int)(opacity * 235), 64, 163, 245), 1.5f * pixelScale))
                {
                    g.FillRectangle(paper, -width / 2, -height / 2, width, height);
                    g.DrawRectangle(edge, -width / 2, -height / 2, width, height);
                    for (int i = 0; i < 3; i++) g.DrawLine(edge, -width * 0.3f, (i - 1) * 8 * pixelScale, width * 0.3f, (i - 1) * 8 * pixelScale);
                }
            }
            g.Restore(state);
        }

        private void DrawSpark(Graphics g, Spark spark)
        {
            float left = 1 - spark.Age / spark.Life;
            float opacity = Math.Max(0, Math.Min(1, left * 2.2f) * Math.Min(1, (FinishTime - eatingAge) * 4));
            float size = spark.Size * (0.55f + left * 0.45f);
            Glow(g, spark.X, spark.Y, size, opacity);
            if (!spark.Star) return;
            float length = size * (2 + 0.4f * (float)Math.Sin(age * 11 + spark.Phase));
            using (Pen ray = new Pen(Color.FromArgb((int)(opacity * 225), 193, 249, 255), Math.Max(0.8f, pixelScale)))
            {
                g.DrawLine(ray, spark.X - length, spark.Y, spark.X + length, spark.Y);
                g.DrawLine(ray, spark.X, spark.Y - length, spark.X, spark.Y + length);
            }
        }

        private static void Glow(Graphics g, float x, float y, float radius, float opacity)
        {
            if (radius <= 0 || opacity <= 0) return;
            opacity = Math.Min(1, opacity);
            using (SolidBrush outer = new SolidBrush(Color.FromArgb((int)(opacity * 24), 32, 126, 255)))
            using (SolidBrush middle = new SolidBrush(Color.FromArgb((int)(opacity * 72), 42, 179, 255)))
            using (SolidBrush core = new SolidBrush(Color.FromArgb((int)(opacity * 235), 155, 241, 255)))
            {
                g.FillEllipse(outer, x - radius * 3, y - radius * 3, radius * 6, radius * 6);
                g.FillEllipse(middle, x - radius * 1.8f, y - radius * 1.8f, radius * 3.6f, radius * 3.6f);
                g.FillEllipse(core, x - radius * 0.62f, y - radius * 0.62f, radius * 1.24f, radius * 1.24f);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (frame != null) { frame.Dispose(); frame = null; }
                if (icon != null) icon.Dispose();
                sparks.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
