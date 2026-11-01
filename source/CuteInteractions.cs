using System;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    internal sealed class CuteInteractionQueue
    {
        internal const double GapSeconds = 2.0;
        internal static readonly string[] Ids = { "pet", "rice", "jump", "idle-breath", "stretch", "yawn", "nap", "think", "daydream", "note", "flowers", "bubbles", "celebrate" };
        internal static readonly string[] Labels = { "摸摸脑袋 ♡", "吃白饭", "跳一下！", "呼吸与眨眼", "伸懒腰", "打哈欠", "打个盹", "认真思考", "发呆", "记笔记", "送花花", "吹泡泡", "开心庆祝" };
        private int next;
        internal double ReadyAt { get; private set; }
        internal bool IsReady(double now) { return now >= ReadyAt; }
        internal void After(double finishedAt) { ReadyAt = finishedAt + GapSeconds; }
        internal void Postpone(double now) { ReadyAt = Math.Max(ReadyAt, now + GapSeconds); }
        internal void Selected(string id)
        {
            int index = Array.IndexOf(Ids, id);
            if (index < 0) throw new ArgumentException("Unknown cute interaction.", "id");
            next = (index + 1) % Ids.Length;
        }
        internal string Next(double now)
        {
            if (!IsReady(now)) throw new InvalidOperationException("Interaction gap has not finished.");
            string id = Ids[next]; Selected(id); return id;
        }
    }

    internal sealed partial class PetForm
    {
        private readonly CuteInteractionQueue cuteQueue = new CuteInteractionQueue();

        private void AddCuteInteractionMenuItems(ToolStripMenuItem interactions)
        {
            dshAnimationsItem = new ToolStripMenuItem("自动轮播可爱互动（间隔 2 秒）", null, delegate { ToggleCuteAutoplay(); })
                { Name = "cute-autoplay", ToolTipText = "每项完整播放后等待 2 秒；手动操作、菜单、拖动和休息期间暂停。" };
            interactions.DropDownItems.Add(dshAnimationsItem);
            interactions.DropDownItems.Add(new ToolStripSeparator());
            for (int i = 0; i < CuteInteractionQueue.Ids.Length; i++)
            {
                string id = CuteInteractionQueue.Ids[i];
                interactions.DropDownItems.Add(new ToolStripMenuItem(CuteInteractionQueue.Labels[i], null,
                    delegate { PlayCuteInteraction(id, false); }) { Name = "cute-" + id, Tag = "cute-action" });
            }
        }

        private void ToggleCuteAutoplay()
        {
            settings.DshAnimations = !settings.DshAnimations;
            PrepareCuteInteraction();
            if (settings.DshAnimations) Wake();
            Say(settings.DshAnimations ? "开始轮播可爱互动啦！\n每个动作结束后休息 2 秒。" : "自动轮播暂停啦。\n仍然可以点选你想看的动作。", 3);
            ScheduleDshAnimations(watch.Elapsed.TotalSeconds);
            RefreshMenuState(); Save(); RenderFrame();
        }

        private void PrepareCuteInteraction()
        {
            CancelDesktopEating(); ResetPetClicks();
            dshAnimationName = null; dshAnimationUntil = 0; ResetNativeAnimationFrame();
            poseName = null; poseUntil = 0; jumpUntil = happyUntil = 0;
            idleSwayUntil = idleStretchUntil = 0;
            walkUntil = 0; walkRemainder = walkVerticalRemainder = 0; followingStep = false;
            viewDirection = previousViewDirection = ViewFront; sideFacing = previousSideFacing = 1; turnUntil = 0;
        }

        private double CuteInteractionEnd(double now)
        {
            return Math.Max(now, Math.Max(dshAnimationUntil, Math.Max(poseUntil,
                Math.Max(jumpUntil, Math.Max(happyUntil, speechUntil)))));
        }

        private void ScheduleCuteInteractionEnd(string id)
        {
            cuteQueue.Selected(id);
            ScheduleDshAnimations(watch.Elapsed.TotalSeconds);
        }

        private void PlayCuteInteraction(string id, bool automatic)
        {
            if (Array.IndexOf(CuteInteractionQueue.Ids, id) < 0) return;
            if (id == "pet") Pet();
            else if (id == "rice") EatRice();
            else if (id == "jump") Jump();
            else
            {
                PrepareCuteInteraction(); Wake();
                if (!automatic)
                    Say(PetName + "：" + CuteInteractionQueue.Labels[Array.IndexOf(CuteInteractionQueue.Ids, id)] + "～", 2.5);
                StartDshAnimation(id, 9.7);
            }
            ScheduleCuteInteractionEnd(id);
            RenderFrame();
        }

        private void PauseAutoplayForMovement()
        {
            PrepareCuteInteraction();
            ScheduleDshAnimations(watch.Elapsed.TotalSeconds);
        }

        private bool CanUseMovementGap(double now)
        {
            return !settings.DshAnimations || now < cuteQueue.ReadyAt && now >= CuteInteractionEnd(now);
        }
    }
}
