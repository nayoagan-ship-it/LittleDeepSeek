using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LittleDeepSeek
{
    internal sealed class PetActionSample
    {
        internal string Pose = "front", Effect;
        internal float Weight, X, Y, Angle, ScaleX = 1, ScaleY = 1;
        internal double Elapsed;
    }

    // Timelines contain semantic poses and motion only. A skin supplies every
    // character image; no timeline is allowed to borrow another skin's pixels.
    internal static class SharedPetAnimations
    {
        internal static readonly string[] Actions = { "idle-breath", "stretch", "yawn", "nap", "think", "daydream", "note", "flowers", "bubbles", "celebrate" };

        internal static bool Contains(string action) { return Array.IndexOf(Actions, action) >= 0; }
        private static float Smooth(double value) { float x = (float)Math.Max(0, Math.Min(1, value)); return x * x * (3 - 2 * x); }

        internal static PetActionSample Sample(string action, double elapsed, double duration)
        {
            if (!Contains(action)) throw new ArgumentException("Unknown action.", "action");
            double t = Math.Max(0, Math.Min(duration, elapsed));
            float envelope = Smooth(t / .28) * Smooth((duration - t) / .28);
            float slow = (float)Math.Sin(t * 2.5), quick = (float)Math.Sin(t * 7.2);
            PetActionSample sample = new PetActionSample { Elapsed = t, Effect = action };
            switch (action)
            {
                case "idle-breath":
                    // Close and reopen the eyes twice in each idle clip.
                    double blink = t % 3.4;
                    sample.Pose = "blink";
                    sample.Weight = Smooth((blink - 1.55) / .07) * Smooth((1.83 - blink) / .07) * envelope;
                    sample.ScaleY = 1 + .012f * slow * envelope;
                    sample.Y = -1.6f * slow * envelope;
                    break;
                case "stretch":
                    sample.Pose = "stretch"; sample.Weight = envelope;
                    sample.ScaleX = 1 - .025f * envelope;
                    sample.ScaleY = 1 + (.035f + .01f * slow) * envelope;
                    sample.Y = -3 * envelope; sample.Angle = 2.7f * slow * envelope;
                    break;
                case "yawn":
                    sample.Pose = "yawn"; sample.Weight = envelope;
                    sample.Angle = (3 + 2 * slow) * envelope;
                    sample.ScaleY = 1 + .018f * slow * envelope;
                    sample.Y = 2.2f * slow * envelope;
                    break;
                case "nap":
                    sample.Pose = "sleep"; sample.Weight = envelope;
                    sample.ScaleY = 1 + .016f * slow * envelope;
                    sample.Y = slow * envelope;
                    break;
                case "think":
                    sample.Pose = "think"; sample.Weight = envelope;
                    sample.Angle = -2.5f * envelope + 1.2f * slow * envelope;
                    sample.X = 1.5f * slow * envelope;
                    break;
                case "daydream":
                    sample.Pose = "daydream"; sample.Weight = envelope;
                    sample.Angle = 3.4f * slow * envelope;
                    sample.Y = -4 * (float)((Math.Sin(t * 1.8) + 1) * .5) * envelope;
                    break;
                case "note":
                    sample.Pose = "note"; sample.Weight = envelope;
                    sample.Angle = .9f * quick * envelope;
                    sample.Y = .7f * quick * envelope;
                    break;
                case "flowers":
                    sample.Pose = "flower"; sample.Weight = envelope;
                    sample.Angle = 2.8f * slow * envelope;
                    sample.Y = -3 * Math.Max(0, quick) * envelope;
                    break;
                case "bubbles":
                    sample.Pose = "bubbles"; sample.Weight = envelope;
                    sample.ScaleX = 1 + .012f * slow * envelope;
                    sample.Angle = 1.2f * slow * envelope;
                    break;
                case "celebrate":
                    sample.Pose = "celebrate"; sample.Weight = envelope;
                    sample.Y = -16 * Math.Max(0, (float)Math.Sin(t * 5.5)) * envelope;
                    sample.Angle = 3.5f * quick * envelope;
                    sample.ScaleY = 1 + .025f * quick * envelope;
                    break;
            }
            return sample;
        }

        private static void DrawPose(Graphics graphics, SkinPose pose, float opacity)
        {
            if (opacity <= .001f) return;
            float height = pose.Height, width = height * pose.Crop.Width / pose.Crop.Height;
            // Very wide sleeping poses stay within the desktop pet's hit area.
            if (width > 246) { height *= 246 / width; width = 246; }
            using (ImageAttributes attributes = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = Math.Max(0, Math.Min(1, opacity));
                attributes.SetColorMatrix(matrix);
                Rectangle destination = Rectangle.Round(new RectangleF(-width / 2, -height, width, height));
                graphics.DrawImage(pose.Image, destination, pose.Crop.X, pose.Crop.Y, pose.Crop.Width, pose.Crop.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        internal static void Draw(Graphics graphics, PetSkin skin, PetActionSample sample)
        {
            if (skin == null || skin.UsesNativeClips) throw new InvalidOperationException("Retargeted animation requires the selected skin's own pose set.");
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(sample.X, sample.Y);
            graphics.RotateTransform(sample.Angle);
            graphics.ScaleTransform(sample.ScaleX, sample.ScaleY);
            DrawPose(graphics, skin.Pose("front"), 1 - sample.Weight);
            DrawPose(graphics, skin.Pose(sample.Pose), sample.Weight);
            DrawEffects(graphics, sample);
            graphics.Restore(state);
        }

        private static void DrawEffects(Graphics g, PetActionSample sample)
        {
            float weight = Math.Max(0, Math.Min(1, sample.Weight));
            if (weight < .01f) return;
            int alpha = (int)(215 * weight);
            double t = sample.Elapsed;
            string effect = sample.Effect;
            if (effect == "bubbles")
            {
                for (int i = 0; i < 5; i++)
                {
                    float progress = (float)((t * .36 + i * .21) % 1);
                    float x = -14 - progress * 70 + (float)Math.Sin(t * 2 + i) * 9;
                    float y = -130 - progress * 111;
                    float size = 8 + progress * 13;
                    using (Pen rim = new Pen(Color.FromArgb((int)(alpha * (1 - progress)), 90, 184, 225), 1.3f))
                        g.DrawEllipse(rim, x, y, size, size);
                }
            }
            else if (effect == "nap" || effect == "yawn")
            {
                using (Font font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, 107, 152, 202)))
                    g.DrawString(effect == "nap" ? "z Z" : "z", font, brush, 65, -190 - (float)Math.Sin(t * 2) * 5);
            }
            else if (effect == "think" || effect == "daydream")
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, 161, 195, 215)))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float radius = 4 + i * 1.5f;
                        g.FillEllipse(brush, -91 + i * 19, -222 - (float)Math.Sin(t * 2 + i) * 4, radius, radius);
                    }
                }
            }
            else if (effect == "celebrate" || effect == "flowers")
            {
                for (int i = 0; i < 6; i++)
                {
                    double a = t * 1.4 + i * Math.PI / 3;
                    float x = (float)Math.Cos(a) * 104;
                    float y = -140 + (float)Math.Sin(a) * 64;
                    Color color = i % 2 == 0 ? Color.FromArgb(alpha, 255, 185, 88) : Color.FromArgb(alpha, 243, 149, 181);
                    using (SolidBrush brush = new SolidBrush(color))
                        g.FillPolygon(brush, new PointF[] { new PointF(x, y - 5), new PointF(x + 2, y - 1), new PointF(x + 5, y), new PointF(x + 2, y + 2), new PointF(x, y + 5), new PointF(x - 2, y + 1), new PointF(x - 5, y), new PointF(x - 2, y - 2) });
                }
            }
        }
    }
}
