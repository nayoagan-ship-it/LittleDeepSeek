using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace LittleDeepSeek
{
    // These selected DSH clips are converted locally to compact transparent GIFs.
    // They are rendered only while LittleDeepSeek is stationary, so its three-view movement stays intact.
    internal sealed partial class PetForm
    {
        private readonly Dictionary<string, Image> dshAnimationImages = new Dictionary<string, Image>();
        private readonly Dictionary<string, MemoryStream> dshAnimationStreams = new Dictionary<string, MemoryStream>();
        private Bitmap dshAnimationFrame;
        private Rectangle dshAnimationCrop;
        private string dshAnimationFrameName, dshAnimationName;
        private string lastDshRandomAnimation;
        private int dshAnimationFrameIndex = -1;
        private double dshAnimationStartedAt, dshAnimationUntil, nextDshAnimation;
        // The source clips are 10 fps.  Play them a touch faster while keeping
        // the change modest enough that breathing and small gestures stay natural.
        private const double DshAnimationPlaybackRate = 1.15;
        private const double DshAnimationFrameRate = 10.0 * DshAnimationPlaybackRate;
        // A very short transition avoids a hard pop without leaving two differently
        // styled character outlines on screen long enough to read as a blurry edge.
        private const double DshAnimationFadeSeconds = 0.16;

        // Match the DSH pet's rhythm: a few quiet breathing beats are mixed
        // between distinct actions instead of endlessly alternating left/right.
        // Repeating idle-breath here gives the action loop a soft landing without
        // forcing the roaming code to pretend it is an animation clip.
        private static readonly string[] DshRandomAnimations =
        {
            "idle-breath", "idle-breath", "idle-breath",
            "stretch", "yawn", "think", "daydream", "note", "bubbles"
        };

        private void InitializeDshAnimations()
        {
            string[] names = { "idle-breath", "stretch", "yawn", "nap", "think", "daydream", "note", "flowers", "bubbles", "celebrate" };
            foreach (string name in names) LoadDshAnimation(name);
        }

        private void LoadDshAnimation(string name)
        {
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("LittleDeepSeek.dsh." + name + ".gif");
            if (source == null) return;
            try
            {
                MemoryStream copy = new MemoryStream();
                byte[] buffer = new byte[8192];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0) copy.Write(buffer, 0, read);
                copy.Position = 0;
                dshAnimationStreams.Add(name, copy);
                dshAnimationImages.Add(name, Image.FromStream(copy, true, true));
            }
            catch { }
            finally { try { source.Dispose(); } catch { } }
        }

        private void ScheduleDshAnimations(double now)
        {
            // Continuous mode: once the pet is free, start (or resume with) an
            // original-art motion immediately instead of waiting for a quiet gap.
            nextDshAnimation = now;
        }

        private void TickDshAnimations(double now)
        {
            if (!settings.DshAnimations) return;
            if (!String.IsNullOrEmpty(dshAnimationName) && now < dshAnimationUntil) return;
            if (!String.IsNullOrEmpty(dshAnimationName)) dshAnimationName = null;
            if (now < nextDshAnimation) return;
            if (!CanShowDshRandomAnimation(now)) return;
            StartDshAnimation(PickDshRandomAnimation(), 9.7);
            // The following Tick starts another real DSH clip as soon as this one
            // finishes. Menus, dragging, sleep, speech, and other interactions
            // still pause the loop through CanShowDshRandomAnimation.
            nextDshAnimation = dshAnimationUntil;
        }

        private bool CanShowDshRandomAnimation(double now)
        {
            return Visible && !settings.Sleep && !menu.Visible && !mouseHeld && !dragging && !spinning && !clickTimer.Enabled
                && !DesktopEatingActive && !RicePoseActive && poseName == null && now >= jumpUntil && now >= happyUntil
                && now >= speechUntil && !settings.FollowMouse && !followingStep && now >= walkUntil;
        }

        private string PickDshChatAnimation()
        {
            string[] choices = { "think", "daydream", "note", "bubbles" };
            return choices[random.Next(choices.Length)];
        }

        private string PickDshRandomAnimation()
        {
            // idle-breath deliberately has several entries to weight it. Exclude
            // every matching entry, not just its first slot, so the pet never
            // chains the same clip back-to-back.
            int available = 0;
            foreach (string name in DshRandomAnimations)
                if (!String.Equals(name, lastDshRandomAnimation, StringComparison.Ordinal)) available++;
            if (available == 0) return lastDshRandomAnimation ?? "idle-breath";
            int pick = random.Next(available);
            foreach (string name in DshRandomAnimations)
            {
                if (String.Equals(name, lastDshRandomAnimation, StringComparison.Ordinal)) continue;
                if (pick-- == 0)
                {
                    lastDshRandomAnimation = name;
                    return name;
                }
            }
            // Defensive fallback for a malformed list; initialization always
            // supplies at least idle-breath and one non-idle action.
            lastDshRandomAnimation = "idle-breath";
            return lastDshRandomAnimation;
        }

        private void StartDshAnimation(string name, double seconds)
        {
            if (!settings.DshAnimations || !dshAnimationImages.ContainsKey(name)) return;
            dshAnimationName = name;
            dshAnimationStartedAt = watch.Elapsed.TotalSeconds;
            // Keep the animation's lifetime in sync with its faster frame clock;
            // otherwise the final frames would loop before the next action starts.
            dshAnimationUntil = dshAnimationStartedAt + seconds / DshAnimationPlaybackRate;
            // A DSH-style random action owns the stage.  Do not let autonomous
            // wandering change the view halfway through and hide the motion.
            walkUntil = 0; walkRemainder = 0; walkVerticalRemainder = 0;
            nextWalk = dshAnimationUntil + 0.45;
            dshAnimationFrameName = null;
            dshAnimationFrameIndex = -1;
        }

        private bool TryGetDshAnimation(double now, out Bitmap frame, out Rectangle crop)
        {
            frame = null; crop = Rectangle.Empty;
            if (!settings.DshAnimations || DesktopEatingActive || RicePoseActive || spinning || mouseHeld || dragging) return false;
            string name = null;
            double elapsed = now;
            // Keep LittleDeepSeek's own high-resolution illustration for ordinary idle and sleep.
            // DSH clips are action inserts, rather than a lower-resolution replacement for the pet.
            if (!String.IsNullOrEmpty(dshAnimationName) && now < dshAnimationUntil)
            {
                name = dshAnimationName;
                elapsed = now - dshAnimationStartedAt;
            }
            if (name == null || !dshAnimationImages.ContainsKey(name)) return false;
            if (!UpdateDshAnimationFrame(name, elapsed)) return false;
            frame = dshAnimationFrame; crop = dshAnimationCrop;
            return frame != null && !crop.IsEmpty;
        }

        private float GetDshAnimationOpacity(double now)
        {
            if (String.IsNullOrEmpty(dshAnimationName) || now >= dshAnimationUntil) return 0;
            double fadeIn = Math.Min(1, Math.Max(0, (now - dshAnimationStartedAt) / DshAnimationFadeSeconds));
            double fadeOut = Math.Min(1, Math.Max(0, (dshAnimationUntil - now) / DshAnimationFadeSeconds));
            return (float)Math.Min(fadeIn, fadeOut);
        }

        private bool IsDshAnimationActive(double now)
        {
            return settings.DshAnimations && !String.IsNullOrEmpty(dshAnimationName)
                && now < dshAnimationUntil && !DesktopEatingActive && !RicePoseActive
                && !spinning && !mouseHeld && !dragging;
        }

        // Fallback only: normally RenderFrame presents the matching DSH GIF frame.
        // If an individual embedded GIF cannot decode, the approved original art
        // still receives a readable motion rather than freezing or disappearing.
        private void GetDshOriginalArtTransform(double now, out float scaleX, out float scaleY, out float swayX, out float bob, out float angle)
        {
            scaleX = 1; scaleY = 1; swayX = 0; bob = 0; angle = 0;
            if (!IsDshAnimationActive(now)) return;
            double elapsed = Math.Max(0, now - dshAnimationStartedAt);
            float slow = (float)Math.Sin(elapsed * 3.1);
            float quick = (float)Math.Sin(elapsed * 7.4);
            switch (dshAnimationName)
            {
                case "stretch":
                    scaleX = 1 - 0.09f * slow;
                    scaleY = 1 + 0.18f * slow;
                    swayX = 7f * slow;
                    bob = -13f * Math.Max(0, slow);
                    break;
                case "yawn":
                    swayX = 9f * slow;
                    bob = 7f * slow;
                    angle = 8f * slow;
                    break;
                case "think":
                case "daydream":
                    swayX = 12f * slow;
                    angle = 10f * slow;
                    bob = 5f * quick;
                    break;
                case "note":
                case "bubbles":
                    swayX = 15f * quick;
                    angle = 9f * quick;
                    bob = -10f * Math.Max(0, quick);
                    break;
                default: // idle-breath, flowers, nap, celebrate
                    scaleX = 1 - 0.05f * slow;
                    scaleY = 1 + 0.09f * slow;
                    swayX = 6f * slow;
                    bob = -5f * slow;
                    break;
            }
        }

        // These are independent overlays, never a replacement character frame.
        // They make the original-art motions as legible as the DSH pet's clips
        // while keeping the approved LittleDeepSeek illustration unchanged.
        private void DrawDshOriginalArtEffects(Graphics g, double now)
        {
            if (!IsDshAnimationActive(now)) return;
            float pulse = (float)((Math.Sin((now - dshAnimationStartedAt) * 4.2) + 1) * 0.5);
            int alpha = 205 + (int)(45 * pulse);
            string name = dshAnimationName ?? "";
            if (name == "stretch")
            {
                using (Pen ray = new Pen(Color.FromArgb(alpha, 129, 186, 255), 2.2f))
                {
                    g.DrawLine(ray, -105, -183, -126, -195);
                    g.DrawLine(ray, 105, -183, 126, -195);
                    g.DrawLine(ray, -96, -140, -123, -140);
                    g.DrawLine(ray, 96, -140, 123, -140);
                }
                return;
            }
            if (name == "yawn")
            {
                using (Font font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush ink = new SolidBrush(Color.FromArgb(alpha, 114, 148, 225)))
                {
                    g.DrawString("z", font, ink, 63, -218 - pulse * 7);
                    g.DrawString("z", font, ink, 81, -244 - pulse * 11);
                }
                return;
            }
            if (name == "think" || name == "daydream")
            {
                using (SolidBrush cloud = new SolidBrush(Color.FromArgb(alpha, 237, 245, 255)))
                using (Pen rim = new Pen(Color.FromArgb(alpha, 125, 177, 245), 1.3f))
                {
                    g.FillEllipse(cloud, -121, -235 - pulse * 5, 35, 24);
                    g.FillEllipse(cloud, -97, -246 - pulse * 5, 43, 32);
                    g.FillEllipse(cloud, -66, -230 - pulse * 5, 29, 22);
                    g.DrawEllipse(rim, -121, -235 - pulse * 5, 35, 24);
                    g.DrawEllipse(rim, -97, -246 - pulse * 5, 43, 32);
                    g.DrawEllipse(rim, -66, -230 - pulse * 5, 29, 22);
                    g.FillEllipse(cloud, -82, -195, 9, 7);
                    g.FillEllipse(cloud, -71, -207, 13, 10);
                }
                return;
            }
            if (name == "note")
            {
                float lift = pulse * 9;
                using (SolidBrush paper = new SolidBrush(Color.FromArgb(alpha, 255, 246, 198)))
                using (Pen rim = new Pen(Color.FromArgb(alpha, 241, 174, 92), 1.4f))
                using (Pen line = new Pen(Color.FromArgb(alpha, 129, 172, 236), 1.5f))
                {
                    g.FillRectangle(paper, 82, -232 - lift, 42, 31);
                    g.DrawRectangle(rim, 82, -232 - lift, 42, 31);
                    g.DrawLine(line, 89, -222 - lift, 117, -222 - lift);
                    g.DrawLine(line, 89, -214 - lift, 111, -214 - lift);
                }
                return;
            }
            if (name == "bubbles")
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha, 166, 218, 255)))
                using (Pen rim = new Pen(Color.FromArgb(alpha, 95, 166, 240), 1.2f))
                {
                    float rise = pulse * 18;
                    g.FillEllipse(fill, -116, -210 - rise, 22, 22); g.DrawEllipse(rim, -116, -210 - rise, 22, 22);
                    g.FillEllipse(fill, -85, -239 - rise, 31, 31); g.DrawEllipse(rim, -85, -239 - rise, 31, 31);
                    g.FillEllipse(fill, -50, -207 - rise, 16, 16); g.DrawEllipse(rim, -50, -207 - rise, 16, 16);
                }
                return;
            }
            using (SolidBrush sparkle = new SolidBrush(Color.FromArgb(alpha, 255, 211, 111)))
            {
                float lift = pulse * 8;
                g.FillEllipse(sparkle, -105, -191 - lift, 8, 8);
                g.FillEllipse(sparkle, 101, -205 + lift, 10, 10);
                g.FillEllipse(sparkle, 116, -159 - lift, 6, 6);
            }
        }

        private bool UpdateDshAnimationFrame(string name, double elapsed)
        {
            Image image = dshAnimationImages[name];
            Guid[] dimensions = image.FrameDimensionsList;
            if (dimensions == null || dimensions.Length == 0) return false;
            FrameDimension dimension = new FrameDimension(dimensions[0]);
            int count = Math.Max(1, image.GetFrameCount(dimension));
            int index = ((int)Math.Floor(Math.Max(0, elapsed) * DshAnimationFrameRate)) % count;
            if (name == dshAnimationFrameName && index == dshAnimationFrameIndex && dshAnimationFrame != null) return true;
            try
            {
                image.SelectActiveFrame(dimension, index);
                // GIFs use a transparent palette entry.  Keep the active frame on a premultiplied
                // ARGB surface all the way through UpdateLayeredWindow; an ordinary ARGB bitmap
                // lets the palette RGB leak through while its alpha is being scaled or faded.
                Bitmap next = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(next))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImageUnscaled(image, 0, 0);
                }
                NormalizeDshFrameAlpha(next);
                Rectangle nextCrop = FindOpaqueBounds(next);
                Bitmap previous = dshAnimationFrame;
                dshAnimationFrame = next;
                dshAnimationCrop = nextCrop;
                dshAnimationFrameName = name;
                dshAnimationFrameIndex = index;
                if (previous != null) previous.Dispose();
                return true;
            }
            catch { return false; }
        }

        private static void NormalizeDshFrameAlpha(Bitmap bitmap)
        {
            Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * bitmap.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int row = data.Stride < 0 ? (bitmap.Height - 1 - y) * stride : y * stride;
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int pixel = row + x * 4;
                        byte alpha = pixels[pixel + 3];
                        if (alpha == 0)
                        {
                            // Transparent palette entries must be transparent black.  Otherwise
                            // their hidden RGB can show as a green/black fringe after GDI scaling.
                            pixels[pixel] = pixels[pixel + 1] = pixels[pixel + 2] = 0;
                        }
                        else if (alpha < 255)
                        {
                            // Preserve the PArgb invariant even with imperfect decoded GIF data.
                            pixels[pixel] = Math.Min(pixels[pixel], alpha);
                            pixels[pixel + 1] = Math.Min(pixels[pixel + 1], alpha);
                            pixels[pixel + 2] = Math.Min(pixels[pixel + 2], alpha);
                        }
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally { bitmap.UnlockBits(data); }
        }

        private void DisposeDshAnimations()
        {
            if (dshAnimationFrame != null) { dshAnimationFrame.Dispose(); dshAnimationFrame = null; }
            foreach (Image image in dshAnimationImages.Values) image.Dispose();
            foreach (MemoryStream stream in dshAnimationStreams.Values) stream.Dispose();
            dshAnimationImages.Clear(); dshAnimationStreams.Clear();
        }
    }
}
