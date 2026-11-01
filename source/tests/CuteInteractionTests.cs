using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class CuteInteractionTests
{
    private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;
    private static object Get(object target, string name) { return target.GetType().GetField(name, Instance).GetValue(target); }
    private static void Set(object target, string name, object value) { target.GetType().GetField(name, Instance).SetValue(target, value); }
    private static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method, Instance).Invoke(target, args); }
    private static double ReadyAt(object queue) { return (double)queue.GetType().GetProperty("ReadyAt", Instance).GetValue(queue, null); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static bool HasLabel(ToolStripItemCollection items, string fragment)
    {
        foreach (ToolStripItem item in items)
        {
            if (item.Text.Contains(fragment)) return true;
            ToolStripMenuItem child = item as ToolStripMenuItem;
            if (child != null && HasLabel(child.DropDownItems, fragment)) return true;
        }
        return false;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root = AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", "settings.ini"), "skin=default\nsleep=0\nwalk=0\nlivelyRoam=0\nidleChat=1\nidleMotions=1\ndshAnimations=1\nhardwareAlerts=0\ntodoReminders=0\n");
            Assembly app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type queueType = app.GetType("LittleDeepSeek.CuteInteractionQueue");
            object queue = Activator.CreateInstance(queueType, true);
            string[] ids = (string[])queueType.GetField("Ids", Static).GetValue(null);
            Check(ids.Length == 13, "Autoplay must contain all 13 cute actions after removing spin");
            Check(Array.IndexOf(ids, "spin") < 0, "Removed spin must not be in autoplay");
            Call(queue, "After", 10.0);
            Check(ReadyAt(queue) == 12.0, "Gap is exactly two seconds after completion");
            Check(!(bool)Call(queue, "IsReady", 11.999), "Next animation must not start before the complete gap");
            Check((bool)Call(queue, "IsReady", 12.0), "Next animation is eligible at exactly two seconds");
            HashSet<string> visited = new HashSet<string>();
            double now = 12;
            for (int i = 0; i < ids.Length; i++)
            {
                string selected = (string)Call(queue, "Next", now);
                Check(selected == ids[i], "Autoplay should cover menu order without random omissions");
                visited.Add(selected); Call(queue, "After", now + 5); now += 7;
            }
            Check(visited.Count == 13, "One full cycle must play every item exactly once");
            Check((string)Call(queue, "Next", now) == ids[0], "Playlist must cycle after the thirteenth action");
            Call(queue, "Postpone", now + 20);
            Check(ReadyAt(queue) == now + 22, "User interruption must leave a fresh two-second gap");
            Call(queue, "Selected", "note");
            Check((string)Call(queue, "Next", now + 22) == "flowers", "After a manual selection, autoplay should continue with the following item");

            using (Form pet = (Form)Activator.CreateInstance(app.GetType("LittleDeepSeek.PetForm"), Instance, null, new object[] { true }, null))
            {
                ContextMenuStrip menu = (ContextMenuStrip)Get(pet, "menu");
                ToolStripMenuItem interactions = (ToolStripMenuItem)menu.Items["cute-interactions"];
                ToolStripMenuItem personalization = (ToolStripMenuItem)menu.Items["personalization"];
                Check(interactions != null && personalization != null, "Both menu groups must remain available");
                foreach (string id in ids)
                {
                    ToolStripMenuItem item = interactions.DropDownItems["cute-" + id] as ToolStripMenuItem;
                    Check(item != null && !item.HasDropDownItems, "Each action must be directly selectable in cute interactions: " + id);
                }
                Check(!HasLabel(personalization.DropDownItems, "预览待机动作"), "Old preview submenu must be removed from personalization");
                Check(!HasLabel(menu.Items, "转圈") && !HasLabel(menu.Items, "画圈"), "Spin menu and gesture hints must be absent");
                foreach (string method in new string[] { "StartSpin", "TrackHeadSpinGesture", "BeginHeadSpinTracking", "DrawSpinOrbit", "IsHeadGrab" })
                    Check(pet.GetType().GetMethod(method, Instance) == null, "Spin implementation must be removed: " + method);
                object settings = Get(pet, "settings");
                queue = Get(pet, "cuteQueue");
                Call(pet, "SwitchSkin", "sample-coral");
                foreach (string id in ids)
                {
                    Call(pet, "PlayCuteInteraction", id, true);
                    double end = Math.Max((double)Get(pet, "dshAnimationUntil"), Math.Max((double)Get(pet, "poseUntil"),
                        Math.Max((double)Get(pet, "jumpUntil"), Math.Max((double)Get(pet, "happyUntil"), (double)Get(pet, "speechUntil")))));
                    Check(Math.Abs(ReadyAt(queue) - end - 2.0) < 1e-8, "Production deadline must be two seconds after the full interaction: " + id);
                    Check((string)Get(settings, "Skin") == "sample-coral", "Autoplay must retain the selected model for all actions");
                }
                Call(pet, "ToggleCuteAutoplay");
                Check(!(bool)Get(settings, "DshAnimations"), "Autoplay toggle must stop automatic scheduling");
                Call(pet, "PlayCuteInteraction", "think", false);
                Check((string)Get(pet, "dshAnimationName") == "think", "Manual actions must still play when autoplay is off");
                Check((bool)Call(pet, "IsDshAnimationActive", ((Stopwatch)Get(pet, "watch")).Elapsed.TotalSeconds), "Manually selected animation must remain visible when autoplay is off");
                Call(pet, "ToggleCuteAutoplay");
                Check((bool)Get(settings, "DshAnimations"), "Autoplay can be enabled again");
                Check(File.ReadAllText(Path.Combine(root, "data", "settings.ini")).Contains("dshAnimations=1"), "Autoplay choice must survive restart");

                // Check real action deadlines and the sampled source clock for every skin.
                Check((double)pet.GetType().GetField("DshAnimationPlaybackRate", Static).GetRawConstantValue() == 1.1, "Cute actions must play at 1.1x");
                Check((double)pet.GetType().GetField("DshAnimationFrameRate", Static).GetRawConstantValue() == 11.0, "Native clips must advance at 11 source frames per second");
                foreach (string skin in new string[] { "default", "sample-coral", "sample-mint" })
                {
                    Call(pet, "SwitchSkin", skin);
                    Check((string)Get(settings, "Skin") == skin, "Imported skin must actually be selected: " + skin);
                    object selectedSkin = Get(pet, "currentSkin");
                    Check((string)Get(selectedSkin, "Name") == (string)Get(pet, "identity").GetType().GetProperty("Name", Instance).GetValue(Get(pet, "identity"), null), "Selected skin name must drive pet identity");
                    foreach (string action in new string[] { "idle-breath", "stretch", "yawn", "nap", "think", "daydream", "note", "flowers", "bubbles", "celebrate" })
                    {
                        Call(pet, "PlayCuteInteraction", action, true);
                        double started = (double)Get(pet, "dshAnimationStartedAt");
                        double duration = (double)Get(pet, "dshAnimationUntil") - started;
                        Check(Math.Abs(duration - 9.7 / 1.1) < 1e-8, "Full clip must finish at 1.1x on " + skin + ": " + action);
                        object sample = Call(pet, "SampleDshAnimation", started + 2.0);
                        Check(Math.Abs((double)Get(sample, "Elapsed") - 2.2) < 1e-8, "Two real seconds must play 2.2 source seconds on " + skin + ": " + action);
                    }
                    Stopwatch clock = (Stopwatch)Get(pet, "watch");
                    double before = clock.Elapsed.TotalSeconds;
                    Call(pet, "EatRice");
                    double after = clock.Elapsed.TotalSeconds;
                    double end = (double)Get(pet, "poseUntil");
                    Check(end >= before + 5.0 / 1.1 - .001 && end <= after + 5.0 / 1.1 + .001, "Rice action must finish at 1.1x on " + skin);
                    before = clock.Elapsed.TotalSeconds;
                    Call(pet, "Jump");
                    after = clock.Elapsed.TotalSeconds;
                    end = (double)Get(pet, "jumpUntil");
                    Check(end >= before + .75 / 1.1 - .001 && end <= after + .75 / 1.1 + .001, "Jump action must finish in 0.75 / 1.1 seconds on " + skin);
                }
                Call(pet, "SwitchSkin", "sample-coral");

                // An ordinary drag beginning over the head follows the same path
                // as body dragging. No mouse input is injected into other apps.
                Point pointer = Control.MousePosition;
                Set(pet, "mouseHeld", true); Set(pet, "dragging", false);
                Set(pet, "downMouse", new Point(pointer.X - 40, pointer.Y)); Set(pet, "downLocation", new Point(100, 100));
                Call(pet, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 1, 132, 151, 0));
                Check((bool)Get(pet, "dragging"), "Dragging from the head should immediately become a normal move");
                Set(pet, "mouseHeld", false); Set(pet, "dragging", false);
                Call(pet, "ToggleFollowMouse");
                Check((bool)Get(settings, "DshAnimations") && (bool)Get(settings, "FollowMouse"), "Changing movement mode must preserve requested autoplay");
                Call(pet, "ToggleCuteAutoplay");
                Check(!(bool)Get(settings, "DshAnimations"), "Autoplay can still be paused explicitly");
                Call(pet, "ToggleCuteAutoplay");
                Check((bool)Get(settings, "DshAnimations"), "Autoplay can be enabled again without changing skin");
            }
            Console.WriteLine("PASS: " + checks + " cute-interaction menu, two-second scheduling and spin-removal checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
