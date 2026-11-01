using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    internal sealed partial class PetForm
    {
        private readonly PetIdentity identity = new PetIdentity();
        internal PetIdentity Identity { get { return identity; } }
        internal string PetName { get { return identity.Name; } }
        private ToolStripMenuItem petTitleItem, petExitItem, skinMenu;
        private PetSkin defaultSkin, currentSkin;
        private Dictionary<string, PetSkin> skinCatalog;
        private readonly Dictionary<string, ToolStripMenuItem> skinMenuItems = new Dictionary<string, ToolStripMenuItem>();
        private bool SkinUsesDshClips { get { return currentSkin != null && currentSkin.UsesNativeClips; } }
        private int invalidSkinCount;
        private string SkinsFolder { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "skins"); } }

        private Dictionary<string, PetSkin> LoadSkinCatalog()
        {
            invalidSkinCount = 0;
            Dictionary<string, PetSkin> catalog = new Dictionary<string, PetSkin>(StringComparer.Ordinal);
            catalog.Add(defaultSkin.Id, defaultSkin);
            if (Directory.Exists(SkinsFolder))
            {
                string[] folders = Directory.GetDirectories(SkinsFolder); Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
                foreach (string folder in folders)
                {
                    // The default artwork is embedded so missing or damaged external files
                    // cannot restore the former character or break default startup.
                    if (String.Equals(Path.GetFileName(folder), PetSkin.DefaultId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(Path.Combine(folder, "skin.json"))) continue;
                    PetSkin skin = null;
                    try
                    {
                        skin = PetSkin.LoadDirectory(folder);
                        if (catalog.ContainsKey(skin.Id)) throw new InvalidDataException("皮肤标识重复。");
                        catalog.Add(skin.Id, skin); skin = null;
                    }
                    catch (Exception error)
                    {
                        if (skin != null) skin.Dispose();
                        invalidSkinCount++;
                        Trace.WriteLine("Skin pack " + Path.GetFileName(folder) + ": " + error.Message);
                    }
                }
            }
            return catalog;
        }

        private void InitializeSkins()
        {
            defaultSkin = PetSkin.LoadDefault();
            skinCatalog = LoadSkinCatalog();
            PetSkin selected;
            if (!skinCatalog.TryGetValue(PetSkin.NormalizeId(settings.Skin), out selected)) selected = defaultSkin;
            ApplySkinArt(selected);
        }

        private void ApplySkinArt(PetSkin skin)
        {
            art = skin.Images[0]; sideArt = skin.Images[1]; backArt = skin.Images[2];
            sleepArt = skin.Images[3]; flowerArt = skin.Images[4]; riceArt = skin.Images[5];
            crop = skin.Bounds[0]; sideCrop = skin.Bounds[1]; backCrop = skin.Bounds[2];
            sleepCrop = skin.Bounds[3]; flowerCrop = skin.Bounds[4]; riceCrop = skin.Bounds[5];
            currentSkin = skin;
            settings.Skin = skin.Id;
            identity.SetName(skin.Name);
        }

        private void SkinNameChanged(object sender, EventArgs e)
        {
            Text = PetName; AccessibleName = PetName + "桌面宠物";
            tray.Text = PetName + " · 右键互动 / todo-list";
            petTitleItem.Text = PetName + "  ·  你的桌面小伙伴";
            petExitItem.Text = "退出" + PetName;
            RefreshMenuState();
        }

        private string GetPetCaption()
        {
            return PetName + (DesktopEatingActive ? (deleteStage == 2 ? "  ·  啊呜！" : "  ·  哒哒哒")
                : RicePoseActive ? "  ·  吃白饭" : settings.Sleep ? "  ·  休息中" : "");
        }

        private ToolStripMenuItem BuildSkinMenu()
        {
            skinMenu = new ToolStripMenuItem("更换皮肤") { Name = "skin-menu" };
            PopulateSkinMenu();
            skinMenu.DropDownOpening += delegate { RefreshSkinMenuState(); };
            return skinMenu;
        }

        private void PopulateSkinMenu()
        {
            while (skinMenu.DropDownItems.Count > 0) { ToolStripItem item = skinMenu.DropDownItems[0]; skinMenu.DropDownItems.RemoveAt(0); item.Dispose(); }
            skinMenuItems.Clear();
            foreach (KeyValuePair<string, PetSkin> entry in skinCatalog)
            {
                string id = entry.Key;
                ToolStripMenuItem item = new ToolStripMenuItem(entry.Value.Name + (id == PetSkin.DefaultId ? "（默认）" : ""), null, delegate { SwitchSkin(id); }) { Name = "skin-" + id };
                skinMenuItems.Add(id, item); skinMenu.DropDownItems.Add(item);
            }
            skinMenu.DropDownItems.Add(new ToolStripSeparator());
            skinMenu.DropDownItems.Add("打开皮肤文件夹", null, delegate { Directory.CreateDirectory(SkinsFolder); Process.Start(SkinsFolder); });
            skinMenu.DropDownItems.Add("刷新皮肤列表", null, delegate { ReloadSkins(); });
            RefreshSkinMenuState();
        }

        private void RefreshSkinMenuState()
        {
            foreach (KeyValuePair<string, ToolStripMenuItem> entry in skinMenuItems) entry.Value.Checked = entry.Key == settings.Skin;
            if (dshAnimationsItem != null) dshAnimationsItem.Text = "自动轮播可爱互动（间隔 2 秒）";
        }

        private void ResetNativeAnimationFrame()
        {
            if (dshAnimationFrame != null) { dshAnimationFrame.Dispose(); dshAnimationFrame = null; }
            dshAnimationFrameName = null; dshAnimationFrameIndex = -1;
        }

        private void SwitchSkin(string id)
        {
            PetSkin next;
            if (!skinCatalog.TryGetValue(PetSkin.NormalizeId(id), out next)) next = defaultSkin;
            if (currentSkin == next) return;
            double now = watch.Elapsed.TotalSeconds;
            bool keepAction = !settings.Sleep && poseName == null && SharedPetAnimations.Contains(dshAnimationName)
                && now >= dshAnimationStartedAt && now < dshAnimationUntil && dshAnimationUntil - now <= 15;
            CancelDesktopEating(); ResetPetClicks();
            if (Capture) Capture = false;
            mouseHeld = dragging = false;
            jumpUntil = 0; happyUntil = 0; turnUntil = 0;
            walkUntil = 0; walkRemainder = walkVerticalRemainder = 0; followingStep = false;
            if (!keepAction) { dshAnimationName = null; dshAnimationUntil = 0; }
            ResetNativeAnimationFrame();
            viewDirection = previousViewDirection = ViewFront; sideFacing = previousSideFacing = 1;
            // Commit artwork and identity together on the WinForms UI thread.
            // The semantic action clock can continue; decoded character frames cannot.
            ApplySkinArt(next);
            nextWalk = keepAction ? dshAnimationUntil + .1 : now + 3;
            if (!keepAction) ScheduleDshAnimations(now);
            Say("已换上" + next.Name + "皮肤！\n动作和名字也一起切换啦。", 3);
            RefreshMenuState(); Save(); RenderFrame();
        }

        private void ReloadSkins()
        {
            string id = settings.Skin;
            Dictionary<string, PetSkin> previous = skinCatalog;
            skinCatalog = LoadSkinCatalog();
            PetSkin selected;
            if (!skinCatalog.TryGetValue(id, out selected)) selected = defaultSkin;
            ResetNativeAnimationFrame(); ApplySkinArt(selected);
            foreach (PetSkin skin in previous.Values) if (skin != defaultSkin) skin.Dispose();
            PopulateSkinMenu(); ApplyMenuStyle(skinMenu.DropDown);
            Say(invalidSkinCount == 0 ? "皮肤列表已更新，动作也准备好啦。" : "皮肤列表已更新。\n有 " + invalidSkinCount + " 个皮肤素材不完整，暂未启用。", 4);
            RefreshMenuState(); Save(); RenderFrame();
        }

        private void DisposeSkins()
        {
            if (skinCatalog == null) return;
            foreach (PetSkin skin in skinCatalog.Values) skin.Dispose();
            skinCatalog.Clear(); currentSkin = defaultSkin = null;
        }
    }
}
