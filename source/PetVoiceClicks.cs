using System;
using System.Drawing;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    internal sealed partial class PetForm
    {
        private int pendingPetClicks;
        private long lastPetClick;
        private Point lastPetClickPoint;

        private void ResetPetClicks()
        {
            clickTimer.Stop();
            pendingPetClicks = 0;
        }

        private void FinishPetClicks()
        {
            int count = pendingPetClicks;
            ResetPetClicks();
            if (count == 1) Pet();
            else if (count == 2) Jump();
        }

        private void RegisterPetClick(Point point)
        {
            long now = watch.ElapsedMilliseconds;
            Size area = SystemInformation.DoubleClickSize;
            if (pendingPetClicks > 0 && (now - lastPetClick > SystemInformation.DoubleClickTime
                || Math.Abs(point.X - lastPetClickPoint.X) > area.Width / 2
                || Math.Abs(point.Y - lastPetClickPoint.Y) > area.Height / 2)) FinishPetClicks();
            pendingPetClicks++;
            lastPetClick = now;
            lastPetClickPoint = point;
            if (pendingPetClicks >= 3)
            {
                ResetPetClicks();
                OpenVoiceTodo();
                return;
            }
            clickTimer.Stop();
            clickTimer.Start();
        }

        private void OpenVoiceTodo()
        {
            ResetPetClicks();
            CancelDesktopEating();
            CancelRicePose();
            Wake();
            walkUntil = 0;
            walkRemainder = 0;
            Say("要用语音记待办吗？\n在对话里选择开启吧～", 6);
            EnsureUserTodoWindow().RequestVoiceInput();
        }
    }
}
