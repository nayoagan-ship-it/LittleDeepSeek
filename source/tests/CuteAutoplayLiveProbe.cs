using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class CuteAutoplayLiveProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root = AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", "settings.ini"), "skin=sample-coral\nx=100\ny=120\nscale=100\ntop=1\nsleep=0\nwalk=1\nlivelyRoam=0\nfollowMouse=0\nidleChat=1\nidleMotions=1\ndshAnimations=1\nhardwareAlerts=0\ntodoReminders=0\neatDeleted=0\n");
            Assembly app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            using (Form pet = (Form)Activator.CreateInstance(app.GetType("LittleDeepSeek.PetForm"), flags, null, new object[] { false }, null))
            using (Timer timer = new Timer { Interval = 20 })
            {
                object queue = pet.GetType().GetField("cuteQueue", flags).GetValue(pet);
                Stopwatch clock = (Stopwatch)pet.GetType().GetField("watch", flags).GetValue(pet);
                int observed = 0, result = 1;
                double previousReady = 0, shownAt = 0, previousStart = 0;
                pet.Shown += delegate
                {
                    // Exercise the real window/message loop off screen so a
                    // timing probe cannot intercept the user's mouse or work.
                    pet.Region = new System.Drawing.Region(System.Drawing.Rectangle.Empty);
                    pet.Location = new System.Drawing.Point(-10000, -10000);
                    pet.TopMost = false;
                    shownAt = clock.Elapsed.TotalSeconds;
                    pet.GetType().GetField("speechUntil", flags).SetValue(pet, 0.0);
                    timer.Start();
                };
                timer.Tick += delegate
                {
                    int next = (int)queue.GetType().GetField("next", flags).GetValue(queue);
                    if (next != observed)
                    {
                        double now = clock.Elapsed.TotalSeconds;
                        if (next != observed + 1) { Console.Error.WriteLine("Autoplay skipped an item."); pet.Close(); return; }
                        if (observed > 0)
                        {
                            double gap = now - (previousReady - 2);
                            Console.WriteLine("Observed gap after action " + observed + ": " + gap.ToString("F3") + " seconds.");
                            if (gap < 1.98 || gap > 3.0) { Console.Error.WriteLine("Unexpected real-time interaction gap."); pet.Close(); return; }
                        }
                        if (observed == 4)
                        {
                            double duration = previousReady - 2 - previousStart;
                            Console.WriteLine("Observed 1.1x idle clip duration: " + duration.ToString("F3") + " seconds.");
                            if (Math.Abs(duration - 9.7 / 1.1) > .08) { Console.Error.WriteLine("Idle clip did not play at 1.1x."); pet.Close(); return; }
                        }
                        previousStart = now;
                        previousReady = (double)queue.GetType().GetProperty("ReadyAt", flags).GetValue(queue, null);
                        observed = next;
                        if (observed >= 5)
                        {
                            Console.WriteLine("PASS: production timer automatically played petting, rice, jump and idle at 1.1x with four verified two-second gaps; idle chat and walking did not interrupt.");
                            result = 0; timer.Stop(); pet.Close();
                        }
                    }
                    if (clock.Elapsed.TotalSeconds - shownAt > 60) { Console.Error.WriteLine("Autoplay live probe timed out."); timer.Stop(); pet.Close(); }
                };
                Application.Run(pet);
                return result;
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
