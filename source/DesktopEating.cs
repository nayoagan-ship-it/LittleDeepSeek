using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    internal sealed partial class PetForm
    {
        private sealed class PendingMeal
        {
            internal DesktopDeletion File;
            internal double Added;
        }

        private DesktopDeletionMonitor desktopMonitor;
        private System.Windows.Forms.Timer desktopTimer;
        private ToolStripMenuItem deleteAnimationItem;
        private readonly Queue<PendingMeal> deletedFiles = new Queue<PendingMeal>();
        private DesktopDeletion currentMeal;
        private DeletionEffectForm deleteEffect;
        // 0 idle, 1 running to the icon, 2 eating, 3 returning home.
        private int deleteStage;
        private float deleteStageTime, deleteTravelDuration;
        private double nextMealExistenceCheck;
        private Point deleteHome, deleteStart, deleteDestination;
        private bool DesktopEatingActive { get { return deleteStage != 0; } }

        private void StartDesktopMonitoring()
        {
            if (desktopMonitor != null || closing) return;
            desktopMonitor = new DesktopDeletionMonitor(Handle);
            desktopMonitor.Deleted += OnDesktopDeleted;
            desktopMonitor.Refresh();
            desktopTimer = new System.Windows.Forms.Timer { Interval = 500 };
            desktopTimer.Tick += delegate { if (desktopMonitor != null) desktopMonitor.Refresh(); };
            desktopTimer.Start();
        }

        private void StopDesktopMonitoring()
        {
            if (desktopTimer != null) { desktopTimer.Stop(); desktopTimer.Dispose(); desktopTimer = null; }
            if (desktopMonitor != null) { desktopMonitor.Dispose(); desktopMonitor = null; }
        }

        private void OnDesktopDeleted(DesktopDeletion file)
        {
            if (closing || !ready || !Visible || !settings.EatDeleted || !file.HasPosition || deletedFiles.Count >= 12)
            { file.Dispose(); return; }
            deletedFiles.Enqueue(new PendingMeal { File = file, Added = watch.Elapsed.TotalSeconds });
        }

        // Coordinates of the mouth in the standing sprite's local drawing space.
        private PointF EatingMouthLocal()
        {
            float ratio = 222f / crop.Height;
            return new PointF(132 + (art.Width * 0.504f - (crop.Left + crop.Width / 2f)) * ratio,
                312 - (crop.Bottom - art.Height * 0.551f) * ratio);
        }

        private Point EatingMouthScreen()
        {
            PointF mouth = EatingMouthLocal();
            return new Point(Left + (int)(mouth.X * units), Top + (int)(mouth.Y * units));
        }

        private void SetDeleteTravel(Point destination, int stage)
        {
            deleteStage = stage; deleteStageTime = 0;
            deleteStart = Location; deleteDestination = ClampToScreen(destination);
            double dx = deleteDestination.X - Left, dy = deleteDestination.Y - Top;
            deleteTravelDuration = (float)Math.Max(0.4, Math.Min(3.5, Math.Sqrt(dx * dx + dy * dy) / (720 * dpiScale)));
        }

        private void StartNextMeal()
        {
            while (deletedFiles.Count > 0)
            {
                PendingMeal pending = deletedFiles.Dequeue();
                // Undo, recreation, or a long interaction should not produce a stale animation.
                if (watch.Elapsed.TotalSeconds - pending.Added > 30 ||
                    System.IO.File.Exists(pending.File.FullPath) || System.IO.Directory.Exists(pending.File.FullPath))
                { pending.File.Dispose(); continue; }
                currentMeal = pending.File;
                nextMealExistenceCheck = watch.Elapsed.TotalSeconds + 0.3;
                if (deleteStage == 0) deleteHome = Location;
                poseName = null; poseUntil = 0; walkUntil = 0; clickTimer.Stop();
                PointF mouth = EatingMouthLocal();
                Point goal = new Point(currentMeal.Center.X - (int)((mouth.X + 46) * units),
                    currentMeal.Center.Y - (int)(mouth.Y * units));
                SetDeleteTravel(goal, 1);
                deleteEffect = new DeletionEffectForm(currentMeal.Icon, currentMeal.Center, dpiScale);
                deleteEffect.Show(this); deleteEffect.Render();
                Say("发现桌面小零食！\n哒哒哒，跑过去～", 5);
                return;
            }
            if (deleteStage != 0) SetDeleteTravel(deleteHome, 3);
        }

        private void TickDesktopEating(float dt)
        {
            if (!settings.EatDeleted || !Visible) { if (DesktopEatingActive) CancelDesktopEating(); return; }
            if (mouseHeld || menu.Visible || RicePoseActive) return;
            if (!DesktopEatingActive) { StartNextMeal(); if (!DesktopEatingActive) return; }
            deleteStageTime += dt;
            if (currentMeal != null && watch.Elapsed.TotalSeconds >= nextMealExistenceCheck)
            {
                nextMealExistenceCheck = watch.Elapsed.TotalSeconds + 0.3;
                if (System.IO.File.Exists(currentMeal.FullPath) || System.IO.Directory.Exists(currentMeal.FullPath))
                { ReleaseMeal(); StartNextMeal(); return; }
            }
            if (deleteStage == 1 || deleteStage == 3)
            {
                float progress = Math.Min(1, deleteStageTime / deleteTravelDuration);
                float smooth = progress * progress * (3 - 2 * progress);
                Location = new Point(deleteStart.X + (int)Math.Round((deleteDestination.X - deleteStart.X) * smooth),
                    deleteStart.Y + (int)Math.Round((deleteDestination.Y - deleteStart.Y) * smooth));
                if (progress >= 1)
                {
                    if (deleteStage == 3)
                    {
                        deleteStage = 0; nextWalk = watch.Elapsed.TotalSeconds + 5; Save();
                        return;
                    }
                    deleteStage = 2; deleteStageTime = 0;
                    deleteEffect.SetMouth(EatingMouthScreen()); deleteEffect.BeginEating();
                    Say("啊呜！文件吃掉啦～\n蓝色星星，散开！", 3);
                }
            }
            if (deleteEffect != null)
            {
                bool finished = deleteEffect.Tick(dt);
                deleteEffect.Render();
                if (deleteStage == 2 && finished)
                {
                    ReleaseMeal(); StartNextMeal();
                }
            }
        }

        private void ReleaseMeal()
        {
            if (deleteEffect != null) { deleteEffect.Close(); deleteEffect.Dispose(); deleteEffect = null; }
            if (currentMeal != null) { currentMeal.Dispose(); currentMeal = null; }
        }

        private void CancelDesktopEating()
        {
            ReleaseMeal();
            while (deletedFiles.Count > 0) deletedFiles.Dequeue().File.Dispose();
            if (deleteStage != 0) nextWalk = watch.Elapsed.TotalSeconds + 5;
            deleteStage = 0;
        }

        private void DrawEatingMouth(Graphics g)
        {
            PointF mouth = EatingMouthLocal();
            float open = 2.2f + (float)(0.5 + 0.5 * Math.Sin(deleteStageTime * 21)) * 4.2f;
            float x = mouth.X - 132, y = mouth.Y - 312;
            using (SolidBrush inside = new SolidBrush(Color.FromArgb(255, 53, 54, 85)))
            using (SolidBrush tongue = new SolidBrush(Color.FromArgb(255, 248, 145, 169)))
            {
                g.FillEllipse(inside, x - 4.2f, y - open / 2, 8.4f, open);
                g.FillEllipse(tongue, x - 2.5f, y + open * 0.08f, 5, open * 0.32f);
            }
        }
    }
}
