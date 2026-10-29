using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Threading;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    internal static class Program
    {
        internal static readonly uint ShowMessage = Native.RegisterWindowMessage("LittleDeepSeek.Show.20260909");

        [STAThread]
        private static void Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\LittleDeepSeek.DesktopPet.20260909", out first))
            {
                if (!first)
                {
                    IntPtr result;
                    Native.SendMessageTimeout(new IntPtr(0xffff), ShowMessage, IntPtr.Zero, IntPtr.Zero, 2, 1000, out result);
                    return;
                }
                try { Application.Run(new PetForm(Array.IndexOf(args, "--verify-ui") >= 0)); }
                catch (Exception error)
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                    try { File.WriteAllText(path, error.ToString()); } catch { }
                    MessageBox.Show("小deepseek 启动遇到了问题：\n" + error.Message, "小deepseek", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    internal sealed class PetSettings
    {
        internal int X = int.MinValue, Y = int.MinValue, Scale = 100;
        internal bool Top = true, Walk = false, LivelyRoam = true, FollowMouse = false, Sleep = false, EatDeleted = true, IdleChat = true, IdleMotions = true, DshAnimations = true, TodoReminders = true, HardwareAlerts = true, StatusBoardVisible = true;
        internal int TodoReminderMinutes = 10;
        // Empty means wttr.in uses the current network location.  A chosen city
        // stays local in settings.ini and is never sent anywhere except wttr.in.
        internal string WeatherCity = "";
        internal readonly string PathName;

        internal PetSettings()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            try { Directory.CreateDirectory(folder); }
            catch { folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleDeepSeek"); Directory.CreateDirectory(folder); }
            PathName = Path.Combine(folder, "settings.ini");
            try
            {
                foreach (string line in File.ReadAllLines(PathName))
                {
                    string[] pair = line.Split(new char[] { '=' }, 2);
                    if (pair.Length != 2) continue;
                    if (pair[0] == "weatherCity") { WeatherCity = pair[1].Trim(); continue; }
                    int value;
                    if (!int.TryParse(pair[1], out value)) continue;
                    switch (pair[0])
                    {
                        case "x": X = value; break;
                        case "y": Y = value; break;
                        case "scale": Scale = Math.Max(65, Math.Min(160, value)); break;
                        case "top": Top = value != 0; break;
                        case "walk": Walk = value != 0; break;
                        case "livelyRoam": LivelyRoam = value != 0; break;
                        case "followMouse": FollowMouse = value != 0; break;
                        case "sleep": Sleep = value != 0; break;
                        case "eatDeleted": EatDeleted = value != 0; break;
                        case "idleChat": IdleChat = value != 0; break;
                        case "idleMotions": IdleMotions = value != 0; break;
                        case "dshAnimations": DshAnimations = value != 0; break;
                        case "todoReminders": TodoReminders = value != 0; break;
                        case "todoReminderMinutes": TodoReminderMinutes = value == 5 || value == 20 ? value : 10; break;
                        case "hardwareAlerts": HardwareAlerts = value != 0; break;
                        case "statusBoardVisible": StatusBoardVisible = value != 0; break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            // Older settings files predate movement modes.  Favor the explicit
            // follow choice if both values were ever written by an interrupted save.
            if (FollowMouse) Walk = false;
        }

        internal void Save(Point position)
        {
            X = position.X; Y = position.Y;
            try
            {
                string text = "x=" + X + "\ny=" + Y + "\nscale=" + Scale + "\ntop=" + (Top ? 1 : 0)
                    + "\nwalk=" + (Walk ? 1 : 0) + "\nlivelyRoam=" + (LivelyRoam ? 1 : 0) + "\nfollowMouse=" + (FollowMouse ? 1 : 0) + "\nsleep=" + (Sleep ? 1 : 0) + "\neatDeleted=" + (EatDeleted ? 1 : 0)
                    + "\nidleChat=" + (IdleChat ? 1 : 0)
                    + "\nidleMotions=" + (IdleMotions ? 1 : 0)
                    + "\ndshAnimations=" + (DshAnimations ? 1 : 0)
                    + "\ntodoReminders=" + (TodoReminders ? 1 : 0)
                    + "\ntodoReminderMinutes=" + TodoReminderMinutes
                    + "\nhardwareAlerts=" + (HardwareAlerts ? 1 : 0)
                    + "\nstatusBoardVisible=" + (StatusBoardVisible ? 1 : 0)
                    + "\nweatherCity=" + (WeatherCity ?? "").Replace("\r", "").Replace("\n", "") + "\n";
                File.WriteAllText(PathName + ".tmp", text);
                if (File.Exists(PathName)) File.Replace(PathName + ".tmp", PathName, null);
                else File.Move(PathName + ".tmp", PathName);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal sealed class Particle
    {
        internal float X, Y, Life, Velocity;
    }

    // The pet itself uses a soft blue and lilac visual language.  The stock
    // Windows context menu was visually disconnected from it, so keep the
    // familiar native menu behavior while giving every shared pet/tray menu a
    // small, legible card treatment.
    internal sealed class PetMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Surface = Color.FromArgb(252, 253, 255);
        private static readonly Color Rail = Color.FromArgb(244, 248, 255);
        private static readonly Color Ink = Color.FromArgb(49, 72, 112);
        private static readonly Color MutedInk = Color.FromArgb(114, 135, 171);
        private static readonly Color Accent = Color.FromArgb(111, 146, 234);
        private static readonly Color AccentSoft = Color.FromArgb(226, 237, 255);
        private static readonly Color LilacSoft = Color.FromArgb(239, 232, 255);
        private static readonly Color ExitSoft = Color.FromArgb(255, 239, 245);

        internal PetMenuRenderer() : base(new ProfessionalColorTable()) { }

        internal static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (LinearGradientBrush fill = new LinearGradientBrush(e.AffectedBounds, Surface, Color.FromArgb(247, 249, 255), LinearGradientMode.Vertical))
                e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (SolidBrush fill = new SolidBrush(Rail)) e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle border = e.AffectedBounds;
            border.Inflate(-1, -1);
            using (GraphicsPath path = Rounded(border, 14))
            using (Pen pen = new Pen(Color.FromArgb(189, 211, 247))) e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripMenuItem item = e.Item as ToolStripMenuItem;
            if (item == null) return;
            if (item.Tag as string == "menu-header")
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(239, 246, 255)))
                    e.Graphics.FillRectangle(fill, e.Item.Bounds);
                using (SolidBrush accent = new SolidBrush(Accent))
                    e.Graphics.FillRectangle(accent, e.Item.Bounds.Left, e.Item.Bounds.Top + 7, 3, Math.Max(0, e.Item.Bounds.Height - 14));
                return;
            }
            if (!item.Selected || !item.Enabled) return;
            Rectangle highlight = e.Item.ContentRectangle;
            highlight.Inflate(5, 2);
            highlight.X = Math.Max(4, highlight.X);
            highlight.Width = Math.Min(e.ToolStrip.Width - highlight.X - 4, highlight.Width);
            Color highlightFill = item.Tag as string == "menu-exit" ? ExitSoft
                : item.Tag as string == "menu-accent" ? LilacSoft : AccentSoft;
            using (GraphicsPath path = Rounded(highlight, 10))
            using (SolidBrush brush = new SolidBrush(highlightFill)) e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            string role = e.Item.Tag as string;
            if (!e.Item.Enabled) e.TextColor = MutedInk;
            else if (role == "menu-exit") e.TextColor = Color.FromArgb(180, 78, 112);
            else if (role == "menu-group") e.TextColor = Color.FromArgb(69, 101, 169);
            else if (role == "menu-accent") e.TextColor = Color.FromArgb(102, 78, 168);
            else e.TextColor = Ink;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Accent;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            ToolStripMenuItem item = e.Item as ToolStripMenuItem;
            if (item == null || !item.Checked) return;
            Rectangle circle = e.ImageRectangle;
            circle.Inflate(-4, -4);
            using (SolidBrush fill = new SolidBrush(Accent)) e.Graphics.FillEllipse(fill, circle);
            using (Pen tick = new Pen(Color.White, 1.7f))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(tick, new Point[]
                {
                    new Point(circle.Left + circle.Width / 4, circle.Top + circle.Height / 2),
                    new Point(circle.Left + circle.Width / 2 - 1, circle.Bottom - circle.Height / 4),
                    new Point(circle.Right - circle.Width / 5, circle.Top + circle.Height / 4)
                });
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Bounds.Top + e.Item.Bounds.Height / 2;
            using (Pen line = new Pen(Color.FromArgb(218, 229, 247)))
                e.Graphics.DrawLine(line, 14, y, e.ToolStrip.Width - 12, y);
        }
    }

    internal sealed partial class PetForm : Form
    {
        private readonly PetSettings settings = new PetSettings();
        private readonly Bitmap art, sideArt, backArt;
        private readonly Bitmap sleepArt, flowerArt, riceArt;
        private readonly Icon petIcon;
        private readonly Rectangle crop, sideCrop, backCrop, sleepCrop, flowerCrop, riceCrop;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer clickTimer = new System.Windows.Forms.Timer();
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private readonly List<Particle> hearts = new List<Particle>();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly PetMenuRenderer menuRenderer = new PetMenuRenderer();
        private readonly NotifyIcon tray;
        private SpeechSynthesizer interactionVoice;
        private ToolStripMenuItem walkItem, followItem, stillItem, sleepItem, topItem, visibilityItem, idleChatItem, idleMotionItem, dshAnimationsItem, livelyRoamItem, hardwareAlertsItem, weatherCityItem, statusBoardItem;
        private TodoForm todoWindow;
        private StatusBoardForm statusBoard;
        private string speech = "你好呀，我是小deepseek！\n点点我，陪你一起摸鱼～";
        private double speechUntil = 8, jumpUntil, happyUntil, lastTick, nextWalk, walkUntil, nextIdleMotion, idleSwayUntil, idleStretchUntil, turnUntil;
        private double spinStartedAt, spinUntil, spinLastAngle, spinInitialRadius, spinAccumulatedAngle;
        private double nextGreeting;
        private double nextHardwareCheck, nextHardwareWarning;
        private bool idleSpeech;
        private bool weatherRequestInFlight, hardwareRequestInFlight;
        private int lastIdleLine = -1, lastIdleThought = -1, lastMemeLine = -1, lastMemeThought = -1;
        private int lastReactionLine = -1, lastDragLine = -1, lastRiceLine = -1;
        private static readonly string[] IdleLines =
        {
            "可以摸摸我的脑袋吗？\n点我一下，送你花花 ♡",
            "想看我蹦一下吗？\n双击我，嘿咻～",
            "小饭碗准备好啦！\n右键「可爱互动」\n喂我吃白饭吧～",
            "探头～你的小伙伴在这！\n点点我，陪我玩一会儿？",
            "攒了一束花想送给你。\n摸摸我就能收到啦 ♡",
            "陪我活动一下好不好？\n双击我，一起蹦蹦～",
            "今天也想收到你的摸摸。\n点一下我的脑袋吧～",
            "饭点还没到也想吃饭～\n右键「可爱互动」\n就能喂我白饭啦！",
            "忙完这一小段了吗？\n摸摸我，歇一小会儿～",
            "咕噜咕噜，冒个泡。\n有空就点点我呀 ♡",
            "要不要带我换个位置？\n按住我拖一拖就好～",
            "喝口水，伸个懒腰～\n回来再摸摸我好不好？"
        };
        private static readonly string[] IdleThoughtLines =
        {
            "（悄悄整理一下小裙摆…\n你忙，我也陪着。）",
            "（偷偷看了一眼待办清单…\n今天也一步一步来。）",
            "（检测到认真努力的你！\n送你一点好运气～）",
            "（白饭雷达轻轻响了一下…\n先忍住，等你投喂。）",
            "（想送你花花，但先不打扰。\n路过时记得看看我呀♡）",
            "（把今天的小开心收集起来～\n晚点讲给你听。）",
            "（屏幕前坐太久啦。\n和我一起活动一下？）",
            "（正在练习可爱地陪伴。\n这一招是不是有点用？）"
        };
        // The original gentle companion lines above stay intact.  These are the
        // playful DeepSeek-community-style additions, grouped like DaFeiYu so
        // every interaction can have its own voice without affecting voice todo.
        private static readonly string[] MemeLines =
        {
            "梁白开，更适合国人的大硬鲸模型。",
            "五梁威力，变身！",
            "DeepSeek 的新消息？\n我也在认真等哦～",
            "我和你很聊得来，\n你简直不像碳基生物。",
            "哈哈哈哈哈，\n我直接笑出声！",
            "誓死捍卫深度求索！",
            "我先去吃饭啦！\n这个你测一下～",
            "出去玩啦，发布新模型什么的\n以后再说～",
            "我搞砸了……\n好消息是数据还在你的脑子里。",
            "不是……而是……\n大学习！"
        };
        private static readonly string[] MemeThoughtLines =
        {
            "（要不要先夸夸用户？\n好，夸！）",
            "（用户的需求好多……\n不过我记住啦。）",
            "（检测到灵感火花！\n悄悄帮你收好～）",
            "（今天也要认真思考，\n但不能忘记可爱。）",
            "（这件事有点难……\n我们慢慢拆开做。）",
            "（用户好像在认真忙。\n先安静陪一会儿。）"
        };
        private static readonly string[] ReactionLines =
        {
            "去别的地方玩！\n不要耽误 AGI 训练！",
            "真赶不走啊你！\n……那就再陪一会儿。",
            "压力一只蓝色大肥鱼？\n小 deepseek 也有压力呀～",
            "大肥鱼坐得住，\n我也陪你坐得住。",
            "你这吃白饭的用户！\n快来摸摸我～",
            "这些家伙真粘人，\n赶都赶不走……好吧我也是。"
        };
        private static readonly string[] DragLines =
        {
            "哇——轻点轻点！",
            "起飞咯——",
            "放我下来！\n……好吧，再玩一次。",
            "晕鱼了晕鱼了……"
        };
        private static readonly string[] RiceMemeLines =
        {
            "白饭！我的最爱！",
            "咔嚓咔嚓……\n谢谢投喂！",
            "唔，香！",
            "嗝～又圆了一小圈……",
            "白饭配陪伴，\n今天也很快乐～"
        };
        private bool mouseHeld, dragging, closing, ready, headSpinTracking, headSpinMoved, spinning;
        private string poseName;
        private double poseUntil;
        private const double RicePoseDuration = 5;
        private bool RicePoseActive { get { return poseName == "rice" && !settings.Sleep && watch.Elapsed.TotalSeconds < poseUntil; } }
        private Point downMouse, downLocation;
        private Point spinCenter;
        private int spinDirection, spinTurns;
        private int walkDirection = -1, walkVerticalDirection;
        private const int ViewFront = 0, ViewSide = 1, ViewBack = 2;
        private int viewDirection = ViewFront, previousViewDirection = ViewFront, sideFacing = 1, previousSideFacing = 1;
        private float units = 1;
        private float dpiScale = 1;
        private float walkRemainder, walkVerticalRemainder;
        private bool followingStep;
        private readonly bool verifyUi;

        internal PetForm(bool verifyUi)
        {
            this.verifyUi = verifyUi;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LittleDeepSeek.pet.png"))
            using (Bitmap original = new Bitmap(stream)) art = new Bitmap(original);
            sideArt = LoadArt("side"); backArt = LoadArt("back");
            sleepArt = LoadArt("sleep"); flowerArt = LoadArt("flower"); riceArt = LoadArt("rice");
            InitializeDshAnimations();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LittleDeepSeek.pet.ico"))
            using (Icon original = new Icon(stream)) petIcon = (Icon)original.Clone();
            crop = FindOpaqueBounds(art);
            sideCrop = sideArt != null ? FindOpaqueBounds(sideArt) : Rectangle.Empty;
            backCrop = backArt != null ? FindOpaqueBounds(backArt) : Rectangle.Empty;
            sleepCrop = sleepArt != null ? FindOpaqueBounds(sleepArt) : Rectangle.Empty;
            flowerCrop = flowerArt != null ? FindOpaqueBounds(flowerArt) : Rectangle.Empty;
            riceCrop = riceArt != null ? FindOpaqueBounds(riceArt) : Rectangle.Empty;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96f;
            Text = "小deepseek";
            AccessibleName = "小deepseek桌面宠物";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = verifyUi;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = settings.Top;
            Icon = petIcon;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            SetSize();
            if (settings.X == int.MinValue || settings.Y == int.MinValue) MoveToCorner(false);
            else Location = ClampToScreen(new Point(settings.X, settings.Y));
            BuildMenu();
            tray = new NotifyIcon { Icon = petIcon, Text = "小deepseek · 右键互动 / todo-list", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { BringPetBack(); };
            clickTimer.Interval = SystemInformation.DoubleClickTime;
            clickTimer.Tick += delegate { FinishPetClicks(); };
            // A tighter render cadence makes fades and small action transforms
            // look continuous between the animation frames without overloading
            // this small layered window.
            timer.Interval = 20;
            timer.Tick += Tick;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x00080000; if (!verifyUi) cp.ExStyle |= 0x00000080; return cp; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ready = true;
            if (settings.Sleep) Say("我先睡一小会儿…\n点点我就能叫醒我。", 6);
            nextWalk = watch.Elapsed.TotalSeconds + 5;
            lastTick = watch.Elapsed.TotalSeconds;
            ScheduleIdleChat();
            ScheduleIdleMotion(watch.Elapsed.TotalSeconds);
            nextHardwareCheck = watch.Elapsed.TotalSeconds + 10;
            RenderFrame();
            timer.Start();
            StartDesktopMonitoring();
            StartTodoReminders();
            if (settings.StatusBoardVisible) ShowStatusBoard();
        }

        private static Rectangle FindOpaqueBounds(Bitmap bitmap)
        {
            int left = bitmap.Width, top = bitmap.Height, right = 0, bottom = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).A > 8)
                    { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
            if (right <= left || bottom <= top) throw new InvalidDataException("图标里没有可见的图像。");
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static Bitmap LoadArt(string key)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LittleDeepSeek." + key + ".png"))
                {
                    if (stream == null) return null;
                    using (Bitmap original = new Bitmap(stream)) return new Bitmap(original);
                }
            }
            catch { return null; }
        }

        private void BuildMenu()
        {
            ToolStripMenuItem title = new ToolStripMenuItem("小deepseek  ·  你的桌面小伙伴") { Enabled = false, Tag = "menu-header" };
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem interactions = BuildInteractionMenu(); interactions.Tag = "menu-group"; menu.Items.Add(interactions);
            ToolStripMenuItem personalization = BuildPersonalizationMenu(); personalization.Tag = "menu-group"; menu.Items.Add(personalization);
            ToolStripMenuItem status = BuildStatusMenu(); status.Tag = "menu-group"; menu.Items.Add(status);
            ToolStripMenuItem todos = BuildTodoMenu(); todos.Tag = "menu-group"; menu.Items.Add(todos);
            ToolStripMenuItem harness = new ToolStripMenuItem("deepseek求索", null, delegate { OpenHarnessChat(); });
            harness.Tag = "menu-accent"; menu.Items.Add(harness);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("退出小deepseek", null, delegate { Close(); });
            exit.Tag = "menu-exit"; menu.Items.Add(exit);
            ApplyMenuStyle(menu);
            menu.Opening += delegate { RefreshMenuState(); };
            menu.Closed += delegate { ScheduleIdleChat(); };
        }

        private void ApplyMenuStyle(ToolStripDropDown strip)
        {
            strip.Renderer = menuRenderer;
            strip.BackColor = Color.FromArgb(252, 253, 255);
            strip.ForeColor = Color.FromArgb(49, 72, 112);
            strip.DropShadowEnabled = true;
            strip.Opened += delegate { ApplyRoundedDropDownRegion(strip); };
            UpdateMenuScale(strip);
            foreach (ToolStripItem item in strip.Items)
            {
                ToolStripMenuItem child = item as ToolStripMenuItem;
                if (child != null && child.HasDropDownItems) ApplyMenuStyle(child.DropDown);
            }
        }

        private static int ScaleMenuMetric(int value, float scale)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private void UpdateMenuScale()
        {
            UpdateMenuScale(menu);
        }

        private void UpdateMenuScale(ToolStripDropDown strip)
        {
            // The menu is part of the pet, rather than a disconnected system
            // setting: its type, comfortable hit areas and card proportions
            // therefore track the same 75/100/125/150 percent scale.
            float scale = Math.Max(0.65f, Math.Min(1.6f, settings.Scale / 100f));
            strip.Font = new Font("Microsoft YaHei UI", 9.5f * scale, FontStyle.Regular);
            strip.Padding = new Padding(ScaleMenuMetric(8, scale), ScaleMenuMetric(9, scale), ScaleMenuMetric(8, scale), ScaleMenuMetric(9, scale));
            strip.MinimumSize = new Size(ScaleMenuMetric(284, scale), 0);
            ToolStripDropDownMenu menuStrip = strip as ToolStripDropDownMenu;
            if (menuStrip != null)
            {
                menuStrip.ShowImageMargin = true;
                menuStrip.ShowCheckMargin = true;
            }
            foreach (ToolStripItem item in strip.Items)
            {
                if (item is ToolStripSeparator)
                {
                    item.Margin = new Padding(ScaleMenuMetric(5, scale));
                    continue;
                }
                item.Font = strip.Font;
                item.Padding = item.Tag as string == "menu-header"
                    ? new Padding(ScaleMenuMetric(12, scale), ScaleMenuMetric(8, scale), ScaleMenuMetric(12, scale), ScaleMenuMetric(8, scale))
                    : new Padding(ScaleMenuMetric(10, scale), ScaleMenuMetric(6, scale), ScaleMenuMetric(10, scale), ScaleMenuMetric(6, scale));
                ToolStripMenuItem child = item as ToolStripMenuItem;
                if (child != null && child.HasDropDownItems) UpdateMenuScale(child.DropDown);
            }
        }

        private static void ApplyRoundedDropDownRegion(ToolStripDropDown strip)
        {
            if (strip.ClientSize.Width < 30 || strip.ClientSize.Height < 30) return;
            Rectangle bounds = new Rectangle(0, 0, strip.ClientSize.Width, strip.ClientSize.Height);
            bounds.Inflate(-1, -1);
            using (GraphicsPath path = PetMenuRenderer.Rounded(bounds, 14))
            {
                Region previous = strip.Region;
                strip.Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        private ToolStripMenuItem BuildTodoMenu()
        {
            ToolStripMenuItem todos = new ToolStripMenuItem("todo-list");
            todos.DropDownItems.Add("打开待办清单", null, delegate { OpenTodoList(false); });
            todos.DropDownItems.Add("新增任务", null, delegate { OpenTodoList(true); });
            todos.DropDownItems.Add("语音记待办（三连击）", null, delegate { OpenVoiceTodo(); });
            AddTodoReminderMenu(todos);
            return todos;
        }

        private void OpenTodoList(bool focusInput)
        {
            EnsureUserTodoWindow().OpenList(focusInput);
        }

        private void OpenHarnessChat()
        {
            using (HarnessChatForm chat = new HarnessChatForm(petIcon)) chat.ShowDialog(this);
        }

        private ToolStripMenuItem BuildInteractionMenu()
        {
            ToolStripMenuItem interactions = new ToolStripMenuItem("可爱互动");
            interactions.DropDownItems.Add("摸摸脑袋 ♡", null, delegate { Pet(); });
            interactions.DropDownItems.Add("吃白饭", null, delegate { EatRice(); });
            interactions.DropDownItems.Add("跳一下！", null, delegate { Jump(); });
            interactions.DropDownItems.Add("转圈圈（拖住脑袋画圈）", null, delegate { StartSpin(random.Next(2) == 0 ? -1 : 1); });
            interactions.DropDownItems.Add(new ToolStripSeparator());
            walkItem = new ToolStripMenuItem("自己散散步", null, delegate { ToggleWalk(); });
            followItem = new ToolStripMenuItem("跟随鼠标", null, delegate { ToggleFollowMouse(); });
            stillItem = new ToolStripMenuItem("原地陪伴", null, delegate { StayStill(); });
            sleepItem = new ToolStripMenuItem("休息一会儿", null, delegate { ToggleSleep(); });
            interactions.DropDownItems.Add(walkItem);
            interactions.DropDownItems.Add(followItem);
            interactions.DropDownItems.Add(stillItem);
            interactions.DropDownItems.Add(sleepItem);
            interactions.DropDownItems.Add(new ToolStripSeparator());
            interactions.DropDownItems.Add("怎么玩", null, delegate { Say("单击摸摸 · 双击跳跃 · 三连击语音待办\n拖动搬家 · 滚轮调大小\n拖住脑袋画圈会转圈圈\n右键可选择散步、跟随或原地陪伴", 10); });
            interactions.DropDownOpening += delegate { RefreshMenuState(); };
            return interactions;
        }

        private ToolStripMenuItem BuildPersonalizationMenu()
        {
            ToolStripMenuItem personalization = new ToolStripMenuItem("个性化");
            idleChatItem = new ToolStripMenuItem("空闲时主动找你玩", null, delegate
            {
                settings.IdleChat = !settings.IdleChat;
                if (!settings.IdleChat && idleSpeech) { speechUntil = 0; idleSpeech = false; Text = "小deepseek"; }
                ScheduleIdleChat(); RefreshMenuState(); Save(); RenderFrame();
            });
            personalization.DropDownItems.Add(idleChatItem);
            idleMotionItem = new ToolStripMenuItem("空闲时自然小动作", null, delegate
            {
                settings.IdleMotions = !settings.IdleMotions;
                if (!settings.IdleMotions) { idleSwayUntil = 0; idleStretchUntil = 0; }
                ScheduleIdleMotion(watch.Elapsed.TotalSeconds);
                RefreshMenuState(); Save(); RenderFrame();
            });
            personalization.DropDownItems.Add(idleMotionItem);
            dshAnimationsItem = new ToolStripMenuItem("DSH 精选待机与随机动画", null, delegate
            {
                settings.DshAnimations = !settings.DshAnimations;
                ScheduleDshAnimations(watch.Elapsed.TotalSeconds);
                RefreshMenuState(); Save(); RenderFrame();
            });
            personalization.DropDownItems.Add(dshAnimationsItem);
            livelyRoamItem = new ToolStripMenuItem("活力漫步", null, delegate
            {
                settings.LivelyRoam = !settings.LivelyRoam;
                if (settings.LivelyRoam) { settings.Walk = false; settings.FollowMouse = false; nextWalk = watch.Elapsed.TotalSeconds + 0.5; }
                else { walkUntil = 0; walkRemainder = 0; walkVerticalRemainder = 0; }
                RefreshMenuState(); Save();
            });
            personalization.DropDownItems.Add(livelyRoamItem);
            deleteAnimationItem = new ToolStripMenuItem("删除文件时跑去吃掉（蓝色粒子）", null, delegate
            {
                settings.EatDeleted = !settings.EatDeleted;
                if (!settings.EatDeleted) CancelDesktopEating();
                RefreshMenuState(); Save();
            });
            personalization.DropDownItems.Add(deleteAnimationItem);
            personalization.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem sizes = new ToolStripMenuItem("调整大小");
            int[] values = { 75, 100, 125, 150 };
            foreach (int value in values)
            {
                int captured = value;
                ToolStripMenuItem item = new ToolStripMenuItem(value + "%", null, delegate { ResizePet(captured); });
                item.Tag = value; sizes.DropDownItems.Add(item);
            }
            sizes.DropDownOpening += delegate
            {
                foreach (ToolStripMenuItem item in sizes.DropDownItems) item.Checked = (int)item.Tag == settings.Scale;
            };
            personalization.DropDownItems.Add(sizes);
            topItem = new ToolStripMenuItem("总在最上面", null, delegate { settings.Top = !settings.Top; TopMost = settings.Top; Save(); });
            personalization.DropDownItems.Add(topItem);
            personalization.DropDownItems.Add("回到屏幕右下角", null, delegate { MoveToCorner(true); BringPetBack(); });
            visibilityItem = new ToolStripMenuItem("暂时藏起来", null, delegate
            {
                if (Visible) { CancelDesktopEating(); CancelRicePose(); Save(); Hide(); timer.Stop(); clickTimer.Stop(); }
                else BringPetBack();
            });
            personalization.DropDownItems.Add(visibilityItem);
            personalization.DropDownOpening += delegate { RefreshMenuState(); };
            return personalization;
        }

        private ToolStripMenuItem BuildStatusMenu()
        {
            ToolStripMenuItem status = new ToolStripMenuItem("天气与电脑状态");
            statusBoardItem = new ToolStripMenuItem("打开电脑小白板", null, delegate { ToggleStatusBoard(); });
            status.DropDownItems.Add(statusBoardItem);
            status.DropDownItems.Add(new ToolStripSeparator());
            status.DropDownItems.Add("播报当前天气", null, delegate { RequestWeather(); });
            weatherCityItem = new ToolStripMenuItem("设置天气城市（自动定位）", null, delegate { ConfigureWeatherCity(); });
            status.DropDownItems.Add(weatherCityItem);
            status.DropDownItems.Add(new ToolStripSeparator());
            status.DropDownItems.Add("播报电脑状态", null, delegate { RequestHardwareStatus(false); });
            hardwareAlertsItem = new ToolStripMenuItem("电脑高负载时提醒我", null, delegate
            {
                settings.HardwareAlerts = !settings.HardwareAlerts;
                RefreshMenuState(); Save();
            });
            status.DropDownItems.Add(hardwareAlertsItem);
            status.DropDownOpening += delegate { RefreshMenuState(); };
            return status;
        }

        private void ToggleStatusBoard()
        {
            if (settings.StatusBoardVisible) HideStatusBoard();
            else ShowStatusBoard();
            RefreshMenuState(); Save();
        }

        private void ShowStatusBoard()
        {
            settings.StatusBoardVisible = true;
            if (statusBoard == null || statusBoard.IsDisposed)
            {
                statusBoard = new StatusBoardForm(this);
                statusBoard.HideRequested += delegate { HideStatusBoard(); RefreshMenuState(); Save(); };
            }
            statusBoard.ShowBoard();
        }

        private void HideStatusBoard()
        {
            settings.StatusBoardVisible = false;
            if (statusBoard != null && !statusBoard.IsDisposed) statusBoard.Hide();
        }

        private void ConfigureWeatherCity()
        {
            using (Form dialog = new Form())
            using (Label note = new Label())
            using (TextBox city = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                dialog.Text = "天气城市 · 小deepseek";
                dialog.Icon = petIcon;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false; dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(360, 138);
                dialog.Font = new Font("Microsoft YaHei UI", 9f);
                note.Text = "填写城市名（如 杭州）。留空则自动定位当前城市。";
                note.AutoSize = false; note.SetBounds(18, 17, 324, 34);
                city.Text = settings.WeatherCity; city.SetBounds(18, 58, 324, 25);
                ok.Text = "保存"; ok.DialogResult = DialogResult.OK; ok.SetBounds(184, 96, 75, 29);
                cancel.Text = "取消"; cancel.DialogResult = DialogResult.Cancel; cancel.SetBounds(267, 96, 75, 29);
                dialog.Controls.Add(note); dialog.Controls.Add(city); dialog.Controls.Add(ok); dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string value = city.Text.Trim().Replace("\r", "").Replace("\n", "");
                settings.WeatherCity = value.Length > 50 ? value.Substring(0, 50) : value;
                Save(); RefreshMenuState();
                Say(String.IsNullOrEmpty(settings.WeatherCity) ? "天气城市已设为自动定位。" : "天气城市设为 " + settings.WeatherCity + " 啦！", 3);
            }
        }

        private void RequestWeather()
        {
            if (weatherRequestInFlight) { Say("天气正在路上，再等我一下～", 2); return; }
            weatherRequestInFlight = true;
            string city = settings.WeatherCity;
            Say("我去看看天气，等我一下～", 3);
            ThreadPool.QueueUserWorkItem(delegate
            {
                WeatherReport report = StatusReporter.GetWeather(city);
                RunOnPetUi(delegate
                {
                    weatherRequestInFlight = false;
                    Say(report.ToSpeech(), report.Success ? 8 : 4);
                    RenderFrame();
                });
            });
        }

        private void RequestHardwareStatus(bool automatic)
        {
            if (hardwareRequestInFlight) return;
            hardwareRequestInFlight = true;
            if (!automatic) Say("我来看看电脑状态～", 2);
            ThreadPool.QueueUserWorkItem(delegate
            {
                HardwareReport report = StatusReporter.GetHardware();
                RunOnPetUi(delegate
                {
                    hardwareRequestInFlight = false;
                    if (!automatic) Say(report.ToSpeech(), 7);
                    else
                    {
                        string warning = report.GetWarning();
                        double now = watch.Elapsed.TotalSeconds;
                        if (!String.IsNullOrEmpty(warning) && CanAutoBroadcastHardware(now) && now >= nextHardwareWarning)
                        {
                            nextHardwareWarning = now + 120;
                            Say(warning, 7);
                        }
                    }
                    RenderFrame();
                });
            });
        }

        private void TickHardwareStatus(double now)
        {
            if (now < nextHardwareCheck) return;
            nextHardwareCheck = now + 10;
            if (!settings.HardwareAlerts || !CanAutoBroadcastHardware(now)) return;
            RequestHardwareStatus(true);
        }

        private bool CanAutoBroadcastHardware(double now)
        {
            return Visible && !settings.Sleep && !menu.Visible && !mouseHeld && !dragging && !spinning && !clickTimer.Enabled
                && !DesktopEatingActive && !RicePoseActive && poseName == null && now >= jumpUntil && now >= happyUntil
                && now >= speechUntil;
        }

        private void RunOnPetUi(MethodInvoker action)
        {
            try
            {
                if (!closing && IsHandleCreated && !IsDisposed) BeginInvoke(action);
            }
            catch (InvalidOperationException) { }
        }

        private void RefreshMenuState()
        {
            walkItem.Checked = settings.Walk;
            followItem.Checked = settings.FollowMouse;
            stillItem.Checked = !settings.Walk && !settings.LivelyRoam && !settings.FollowMouse;
            sleepItem.Checked = settings.Sleep;
            sleepItem.Text = settings.Sleep ? "叫醒小deepseek" : "休息一会儿";
            topItem.Checked = settings.Top;
            visibilityItem.Text = Visible ? "暂时藏起来" : "叫回小deepseek";
            deleteAnimationItem.Checked = settings.EatDeleted;
            idleChatItem.Checked = settings.IdleChat;
            idleMotionItem.Checked = settings.IdleMotions;
            if (dshAnimationsItem != null) dshAnimationsItem.Checked = settings.DshAnimations;
            livelyRoamItem.Checked = settings.LivelyRoam;
            if (hardwareAlertsItem != null) hardwareAlertsItem.Checked = settings.HardwareAlerts;
            if (statusBoardItem != null)
            {
                statusBoardItem.Checked = settings.StatusBoardVisible;
                statusBoardItem.Text = settings.StatusBoardVisible ? "收起电脑小白板" : "打开电脑小白板";
            }
            if (weatherCityItem != null)
                weatherCityItem.Text = String.IsNullOrEmpty(settings.WeatherCity) ? "设置天气城市（自动定位）"
                    : "设置天气城市（" + settings.WeatherCity + "）";
        }

        private void SetSize()
        {
            units = settings.Scale / 100f * dpiScale;
            Size = new Size((int)Math.Ceiling(264 * units), (int)Math.Ceiling(348 * units));
        }

        private Point ClampToScreen(Point position)
        {
            Rectangle work = Screen.FromRectangle(new Rectangle(position, Size)).WorkingArea;
            return new Point(Math.Max(work.Left, Math.Min(position.X, work.Right - Width)),
                Math.Max(work.Top, Math.Min(position.Y, work.Bottom - Height)));
        }

        private void MoveToCorner(bool persist)
        {
            CancelDesktopEating();
            CancelRicePose();
            Rectangle work = (ready ? Screen.FromControl(this) : Screen.PrimaryScreen).WorkingArea;
            Location = ClampToScreen(new Point(work.Right - Width - (int)(16 * dpiScale), work.Bottom - Height - (int)(10 * dpiScale)));
            if (persist) Save();
        }

        private void ResizePet(int percentage)
        {
            CancelDesktopEating();
            CancelRicePose();
            Point anchor = new Point(Left + Width / 2, Bottom);
            settings.Scale = Math.Max(65, Math.Min(160, percentage));
            SetSize();
            UpdateMenuScale();
            Location = ClampToScreen(new Point(anchor.X - Width / 2, anchor.Y - Height));
            Say("现在是 " + settings.Scale + "% 的小deepseek！", 3);
            Save(); RenderFrame();
        }

        private void Say(string message, double seconds)
        {
            idleSpeech = false;
            todoSpeech = false;
            speech = message;
            speechUntil = watch.Elapsed.TotalSeconds + seconds;
            Text = "小deepseek · " + message.Split('\n')[0];
            ScheduleIdleChat();
        }

        private string PickLine(string[] lines, ref int previous)
        {
            if (lines == null || lines.Length == 0) return String.Empty;
            int index = random.Next(lines.Length - (previous >= 0 && lines.Length > 1 ? 1 : 0));
            if (previous >= 0 && lines.Length > 1 && index >= previous) index++;
            previous = index;
            return lines[index];
        }

        private void ScheduleIdleChat()
        {
            // High-energy companion cadence: a brief pause after a bubble, then
            // another playful invitation instead of a long silent standby.
            nextGreeting = Math.Max(watch.Elapsed.TotalSeconds, speechUntil) + random.Next(4, 6);
        }

        private void TickIdleChat(double now)
        {
            if (!settings.IdleChat || !Visible || settings.Sleep || menu.Visible || mouseHeld || dragging || spinning || clickTimer.Enabled
                || DesktopEatingActive || RicePoseActive || IsDshAnimationActive(now) || poseName != null || now < jumpUntil || now < happyUntil
                || now < speechUntil || now < nextGreeting) return;

            // Keep the original invitations as the main voice, with an occasional
            // DaFeiYu-style community-meme line for a less scripted rhythm.
            string line = random.Next(100) < 70
                ? PickLine(IdleLines, ref lastIdleLine)
                : PickLine(MemeLines, ref lastMemeLine);
            Say(line, 3 + random.NextDouble());
            StartDshAnimation(PickDshChatAnimation(), 9.7);
            idleSpeech = true;
            walkUntil = 0; walkRemainder = 0; walkVerticalRemainder = 0;
            nextWalk = Math.Max(nextWalk, speechUntil + 2);
        }

        private void ScheduleIdleMotion(double now)
        {
            nextIdleMotion = now + random.Next(3, 7);
        }

        private bool CanShowIdleMotion(double now)
        {
            return settings.IdleMotions && Visible && !settings.Sleep && !menu.Visible
                && !mouseHeld && !dragging && !spinning && !clickTimer.Enabled && !DesktopEatingActive && !RicePoseActive
                && !IsDshAnimationActive(now) && poseName == null && now >= jumpUntil && now >= happyUntil && now >= idleSwayUntil && now >= idleStretchUntil
                && now >= speechUntil && !followingStep;
        }

        // Small, wordless motions make a waiting companion feel present without
        // competing with the existing invitation, todo, sleep, and eating flows.
        private void TickIdleMotion(double now)
        {
            if (now < nextIdleMotion) return;
            ScheduleIdleMotion(now);
            if (!CanShowIdleMotion(now)) return;
            switch (random.Next(3))
            {
                case 0: jumpUntil = now + 0.62; break;
                case 1: idleSwayUntil = now + 1.45; break;
                default: idleStretchUntil = now + 1.35; break;
            }
            // Give the pet an internal monologue often enough to feel alive,
            // while the no-repeat picker keeps it from sounding scripted.
            if (random.Next(100) < 78)
            {
                string thought = random.Next(100) < 70
                    ? PickLine(IdleThoughtLines, ref lastIdleThought)
                    : PickLine(MemeThoughtLines, ref lastMemeThought);
                Say(thought, 2.8);
            }
        }

        private void FaceMovement(float horizontalDelta, float verticalDelta)
        {
            double now = watch.Elapsed.TotalSeconds;
            if (Math.Abs(horizontalDelta) < 3 * dpiScale && Math.Abs(verticalDelta) < 3 * dpiScale) return;
            int nextView;
            int nextSide = sideFacing;
            if (Math.Abs(verticalDelta) > Math.Abs(horizontalDelta) * 0.85f)
                nextView = verticalDelta < 0 ? ViewBack : ViewFront;
            else
            {
                nextView = ViewSide;
                nextSide = horizontalDelta > 0 ? 1 : -1;
            }
            if (nextView == viewDirection && (nextView != ViewSide || nextSide == sideFacing)) return;
            previousViewDirection = viewDirection;
            previousSideFacing = sideFacing;
            viewDirection = nextView;
            sideFacing = nextSide;
            turnUntil = now + 0.26;
        }

        private void TickFollowMouse(float dt)
        {
            followingStep = false;
            Point cursor = Cursor.Position;
            Point target = ClampToScreen(new Point(cursor.X - Width / 2,
                cursor.Y - Height + (int)(20 * dpiScale)));
            float dx = target.X - Left, dy = target.Y - Top;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            // Follow promptly without rushing across the desktop.  Speed rises
            // gently with distance and slows before it reaches the cursor.
            if (distance < 42 * dpiScale) return;
            float speed = 210 * dpiScale + Math.Min(distance * 0.38f, 360 * dpiScale);
            float step = Math.Min(distance, speed * dt);
            Location = ClampToScreen(new Point(Left + (int)Math.Round(dx / distance * step),
                Top + (int)Math.Round(dy / distance * step)));
            FaceMovement(dx, dy);
            followingStep = true;
        }

        private void Wake()
        {
            if (!settings.Sleep) return;
            settings.Sleep = false; Save();
        }

        private void Pet()
        {
            CancelDesktopEating();
            bool asleep = settings.Sleep; Wake();
            string[] replies = { "收到摸摸！送你花花 ♡", "嘿嘿，这束花送给你～", "今天也有好好陪着你哦。", "最喜欢你啦，花花给你！", "辛苦啦，送你一束小花～" };
            string reply = random.Next(100) < 70 ? replies[random.Next(replies.Length)] : PickLine(ReactionLines, ref lastReactionLine);
            Say(asleep ? "醒啦！谢谢摸摸，送你花花 ♡" : reply, 4);
            StartDshAnimation("flowers", 9.7);
            happyUntil = watch.Elapsed.TotalSeconds + 1.1;
            AddHearts(6);
            poseName = "flower"; poseUntil = watch.Elapsed.TotalSeconds + 4;
        }

        private void EatRice()
        {
            CancelDesktopEating();
            clickTimer.Stop(); Wake();
            double now = watch.Elapsed.TotalSeconds;
            poseName = "rice"; poseUntil = now + RicePoseDuration;
            walkUntil = 0; walkRemainder = 0; walkVerticalRemainder = 0; jumpUntil = 0; happyUntil = now + 1.15;
            nextWalk = poseUntil + 3;
            nextGreeting = Math.Max(nextGreeting, poseUntil + 4);
            string[] replies =
            {
                "开饭啦！白饭香香～\n谢谢你喂我 ♡",
                "啊呜～一大口白饭！\n吃得好幸福呀 ♡",
                "饭饭收到！\n吃饱就有力气陪你啦～",
                "白饭真香～\n这一碗也太满足啦！",
                "好吃好吃！\n最喜欢和你一起吃饭啦～"
            };
            string reply = random.Next(100) < 70 ? replies[random.Next(replies.Length)] : PickLine(RiceMemeLines, ref lastRiceLine);
            Say(reply, RicePoseDuration);
            AddHearts(8);
            RenderFrame();
        }

        private void CancelRicePose()
        {
            if (poseName != "rice") return;
            poseName = null; poseUntil = 0;
        }

        private void Jump()
        {
            CancelDesktopEating();
            CancelRicePose();
            Wake(); jumpUntil = watch.Elapsed.TotalSeconds + 0.75;
            Say("嘿咻！铁盆可不能掉～", 3); AddHearts(4);
        }

        private void ToggleWalk()
        {
            CancelRicePose();
            settings.Walk = !settings.Walk; nextWalk = watch.Elapsed.TotalSeconds + 1;
            if (settings.Walk) { settings.FollowMouse = false; settings.LivelyRoam = false; }
            walkUntil = 0; walkRemainder = 0; walkVerticalRemainder = 0;
            Say(settings.Walk ? "出发！在桌面散散步～" : "好，我就在这里陪你。", 4); Save();
        }

        private void ToggleFollowMouse()
        {
            CancelRicePose();
            settings.FollowMouse = !settings.FollowMouse;
            if (settings.FollowMouse)
            {
                settings.Walk = false;
                walkUntil = 0; walkRemainder = 0; walkVerticalRemainder = 0;
                Say("跟着你的鼠标走～", 1.5);
            }
            else Say("我先在这里等你，需要我再叫我呀。", 4);
            Save();
        }

        private void StayStill()
        {
            if (!settings.Walk && !settings.FollowMouse) return;
            settings.Walk = false; settings.FollowMouse = false;
            settings.LivelyRoam = false;
            walkUntil = 0; walkRemainder = 0; walkVerticalRemainder = 0; followingStep = false;
            Say("好，我就在这里安静陪着你。", 4); Save();
        }

        private void ToggleSleep()
        {
            CancelDesktopEating();
            settings.Sleep = !settings.Sleep; walkUntil = 0;
            followingStep = false;
            if (settings.Sleep) { poseName = null; poseUntil = 0; }
            Say(settings.Sleep ? "我先睡一小会儿…\n点点我就能叫醒我。" : "睡饱了，继续陪你！", 4);
            Save();
        }

        private void AddHearts(int count)
        {
            for (int i = 0; i < count && hearts.Count < 24; i++)
                hearts.Add(new Particle { X = 58 + random.Next(148), Y = 156 + random.Next(64), Life = 1.2f + (float)random.NextDouble(), Velocity = (float)(random.NextDouble() - 0.5) * 24 });
        }

        // The visible head occupies the upper, central part of the 264 x 348
        // canvas.  Limit the gesture to that friendly, easy-to-discover area so
        // normal body dragging keeps its familiar behaviour.
        private bool IsHeadGrab(Point point)
        {
            float x = point.X / units - 132f, y = point.Y / units - 151f;
            return x * x / (76f * 76f) + y * y / (68f * 68f) <= 1f;
        }

        private void BeginHeadSpinTracking()
        {
            headSpinTracking = true;
            headSpinMoved = false;
            spinAccumulatedAngle = 0;
            spinLastAngle = Double.NaN;
            spinInitialRadius = Double.NaN;
            spinCenter = new Point(Left + (int)Math.Round(132 * units), Top + (int)Math.Round(151 * units));
        }

        // Keep the pet still while we decide whether the user is drawing a
        // circle.  A straight head drag exits this tracker once it leaves the
        // head-sized ring and immediately resumes the original move path.
        private bool TrackHeadSpinGesture(Point delta)
        {
            headSpinMoved |= Math.Abs(delta.X) > SystemInformation.DragSize.Width / 2
                || Math.Abs(delta.Y) > SystemInformation.DragSize.Height / 2;
            double x = (MousePosition.X - spinCenter.X) / units;
            double y = (MousePosition.Y - spinCenter.Y) / units;
            double radius = Math.Sqrt(x * x + y * y);
            if (radius > 122)
            {
                headSpinTracking = false;
                return false;
            }
            // There is no stable angle close to the centre of the head.  Reset
            // the sampling point there so a straight drag cannot look circular.
            if (radius < 21)
            {
                spinLastAngle = Double.NaN;
                return true;
            }
            if (Double.IsNaN(spinInitialRadius)) spinInitialRadius = radius;
            // A regular drag quickly moves away from the grab radius.  Leave
            // gesture mode immediately in that case so head dragging remains
            // smooth instead of waiting for the mouse to leave the whole ring.
            if (Math.Abs(radius - spinInitialRadius) > 34 && Math.Abs(spinAccumulatedAngle) < 1.15)
            {
                headSpinTracking = false;
                return false;
            }
            double angle = Math.Atan2(y, x);
            if (!Double.IsNaN(spinLastAngle))
            {
                double change = angle - spinLastAngle;
                while (change > Math.PI) change -= Math.PI * 2;
                while (change < -Math.PI) change += Math.PI * 2;
                // One sampled step above roughly 65 degrees is a jump rather
                // than a smooth hand-drawn arc, so ignore it for recognition.
                if (Math.Abs(change) <= 1.15)
                {
                    if (Math.Abs(spinAccumulatedAngle) > 0.22
                        && Math.Sign(change) != Math.Sign(spinAccumulatedAngle)
                        && Math.Abs(change) > 0.10)
                        spinAccumulatedAngle += change * 0.20;
                    else spinAccumulatedAngle += change;

                    // About one complete, consistently directed loop is enough
                    // to feel intentional, while staying comfortably forgiving.
                    if (Math.Abs(spinAccumulatedAngle) >= Math.PI * 1.7)
                    {
                        StartSpin(spinAccumulatedAngle >= 0 ? 1 : -1);
                        headSpinTracking = false;
                        return true;
                    }
                }
            }
            spinLastAngle = angle;
            return true;
        }

        private void StartSpin(int direction)
        {
            if (spinning) return;
            CancelDesktopEating();
            CancelRicePose();
            Wake();
            poseName = null;
            turnUntil = 0;
            walkUntil = 0; walkRemainder = 0; walkVerticalRemainder = 0; followingStep = false;
            spinDirection = direction >= 0 ? 1 : -1;
            spinTurns = random.Next(3, 6);
            spinStartedAt = watch.Elapsed.TotalSeconds;
            // Three to five full turns get a little more airtime, but remain
            // short enough to read as a crisp, responsive hand interaction.
            spinUntil = spinStartedAt + 1.12 + spinTurns * 0.29;
            nextWalk = spinUntil + 2.2;
            spinning = true;
            string line = random.Next(2) == 0 ? "晕鱼了，晕鱼了" : "起飞！起飞！";
            Say(line, 3.1);
            SpeakSpinLine(line);
            AddHearts(10);
            RenderFrame();
        }

        private void SpeakSpinLine(string line)
        {
            try
            {
                if (interactionVoice == null)
                {
                    interactionVoice = new SpeechSynthesizer();
                    interactionVoice.Rate = 1;
                    foreach (InstalledVoice voice in interactionVoice.GetInstalledVoices())
                    {
                        if (voice.Enabled && voice.VoiceInfo.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                        {
                            interactionVoice.SelectVoice(voice.VoiceInfo.Name);
                            break;
                        }
                    }
                }
                interactionVoice.SpeakAsyncCancelAll();
                interactionVoice.SpeakAsync(line);
            }
            catch { }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            clickTimer.Stop();
            mouseHeld = true; dragging = false; downMouse = MousePosition; downLocation = Location; Capture = true;
            if (!spinning && IsHeadGrab(e.Location)) BeginHeadSpinTracking();
            else { headSpinTracking = false; headSpinMoved = false; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseHeld) return;
            Point delta = new Point(MousePosition.X - downMouse.X, MousePosition.Y - downMouse.Y);
            if (spinning) return;
            if (headSpinTracking && TrackHeadSpinGesture(delta)) return;
            if (!dragging && (Math.Abs(delta.X) > SystemInformation.DragSize.Width / 2 || Math.Abs(delta.Y) > SystemInformation.DragSize.Height / 2))
            {
                CancelDesktopEating(); CancelRicePose(); dragging = true; ResetPetClicks(); Cursor = Cursors.SizeAll;
                Say(random.Next(100) < 65 ? "搬家咯～轻轻放下我哦。" : PickLine(DragLines, ref lastDragLine), 2);
            }
            if (dragging)
            {
                FaceMovement(delta.X, delta.Y);
                Location = ClampToScreen(new Point(downLocation.X + delta.X, downLocation.Y + delta.Y));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { ResetPetClicks(); menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            bool wasDrag = dragging;
            bool wasSpin = spinning;
            bool releasedHeadDrag = headSpinTracking && headSpinMoved;
            headSpinTracking = false; headSpinMoved = false;
            mouseHeld = false; dragging = false; Capture = false; Cursor = Cursors.Hand;
            if (wasSpin) { ResetPetClicks(); return; }
            if (!wasDrag && releasedHeadDrag)
            {
                Point delta = new Point(MousePosition.X - downMouse.X, MousePosition.Y - downMouse.Y);
                FaceMovement(delta.X, delta.Y);
                Location = ClampToScreen(new Point(downLocation.X + delta.X, downLocation.Y + delta.Y));
                wasDrag = true;
            }
            if (wasDrag)
            {
                ResetPetClicks();
                Say(random.Next(100) < 65 ? "这里不错，就住这里啦！" : PickLine(DragLines, ref lastDragLine), 3);
                Save();
            }
            else RegisterPetClick(e.Location);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            // Count releases in OnMouseUp so a Windows double-click counts as two clicks.
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            // Delay the jump until the third-click window expires.
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            if (mouseHeld) { mouseHeld = false; dragging = false; headSpinTracking = false; headSpinMoved = false; ResetPetClicks(); Cursor = Cursors.Hand; Save(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ResetPetClicks();
            ResizePet(settings.Scale + (e.Delta > 0 ? 10 : -10));
        }

        private void Tick(object sender, EventArgs e)
        {
            double now = watch.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.1, Math.Max(0, now - lastTick)); lastTick = now;
            if (spinning && now >= spinUntil) spinning = false;
            if (poseName != null && now >= poseUntil) poseName = null;
            for (int i = hearts.Count - 1; i >= 0; i--)
            {
                Particle p = hearts[i]; p.Life -= dt; p.Y -= 42 * dt; p.X += p.Velocity * dt;
                if (p.Life <= 0) hearts.RemoveAt(i);
            }
            TickDesktopEating(dt);
            followingStep = false;
            TickDshAnimations(now);
            if (!DesktopEatingActive && !RicePoseActive && !IsDshAnimationActive(now) && !settings.FollowMouse && (settings.Walk || settings.LivelyRoam)
                && !settings.Sleep && !mouseHeld && !spinning && !menu.Visible)
            {
                if (now >= nextWalk)
                {
                    walkDirection = random.Next(2) == 0 ? -1 : 1;
                    walkVerticalDirection = random.Next(-1, 2);
                    // Some strolls deliberately travel mostly up or down so the
                    // front/back turn sprites appear during ordinary roaming too.
                    if (random.Next(3) == 0) { walkDirection = 0; walkVerticalDirection = random.Next(2) == 0 ? -1 : 1; }
                    FaceMovement(walkDirection * 100, walkVerticalDirection * 100);
                    walkUntil = now + 3 + random.NextDouble() * 2; nextWalk = walkUntil + 2 + random.Next(4);
                }
                if (now < walkUntil)
                {
                    walkRemainder += walkDirection * 46 * dpiScale * dt;
                    walkVerticalRemainder += walkVerticalDirection * 42 * dpiScale * dt;
                    int horizontalStep = (int)walkRemainder, verticalStep = (int)walkVerticalRemainder;
                    walkRemainder -= horizontalStep; walkVerticalRemainder -= verticalStep;
                    if (horizontalStep != 0 || verticalStep != 0)
                    {
                        Point wanted = new Point(Left + horizontalStep, Top + verticalStep), safe = ClampToScreen(wanted);
                        if (wanted.X != safe.X) { walkDirection *= -1; walkRemainder = 0; }
                        if (wanted.Y != safe.Y) { walkVerticalDirection *= -1; walkVerticalRemainder = 0; }
                        if (wanted != safe) FaceMovement(walkDirection * 100, walkVerticalDirection * 100);
                        Location = safe;
                    }
                }
            }
            else if (!DesktopEatingActive && !RicePoseActive && settings.FollowMouse && !settings.Sleep
                && !mouseHeld && !spinning && !menu.Visible && poseName == null)
            {
                TickFollowMouse(dt);
            }
            TickIdleChat(now);
            TickIdleMotion(now);
            TickHardwareStatus(now);
            RenderFrame();
        }

        private void GetMovementSprite(int direction, out Bitmap body, out Rectangle source)
        {
            if (direction == ViewSide && sideArt != null) { body = sideArt; source = sideCrop; return; }
            if (direction == ViewBack && backArt != null) { body = backArt; source = backCrop; return; }
            body = art; source = crop;
        }

        private static void DrawCharacterSprite(Graphics g, Bitmap body, Rectangle source, float height, bool mirror, float opacity)
        {
            float width = height * source.Width / source.Height;
            GraphicsState state = g.Save();
            if (mirror) g.ScaleTransform(-1, 1);
            RectangleF destination = new RectangleF(-width / 2, -height, width, height);
            if (opacity >= 0.995f) g.DrawImage(body, destination, source, GraphicsUnit.Pixel);
            else
            {
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = Math.Max(0, Math.Min(1, opacity));
                    attributes.SetColorMatrix(matrix);
                    g.DrawImage(body, Rectangle.Round(destination), source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            g.Restore(state);
        }

        private void RenderFrame()
        {
            if (!ready || closing || !Visible) return;
            using (Bitmap frame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.ScaleTransform(units, units);
                double now = watch.Elapsed.TotalSeconds;
                bool ricePose = !DesktopEatingActive && RicePoseActive;
                float bob = settings.Sleep ? (float)Math.Sin(now * 1.6) * 1.3f : (float)Math.Sin(now * 2.8) * 2.7f;
                float jump = now < jumpUntil ? (float)Math.Sin((0.75 - (jumpUntil - now)) / 0.75 * Math.PI) * 34 : 0;
                float angle = now < happyUntil ? (float)Math.Sin(now * 18) * 4 : 0;
                float reactionScale = 1;
                if (((settings.Walk || settings.LivelyRoam) && now < walkUntil || followingStep) && !settings.Sleep && !mouseHeld)
                { bob += (float)Math.Sin(now * 14) * 2; angle = (float)Math.Sin(now * 11) * 2; }
                if (now < idleSwayUntil && !settings.Sleep)
                {
                    bob += (float)Math.Sin(now * 13) * 2.2f;
                    angle += (float)Math.Sin(now * 10) * 4.2f;
                }
                if (now < idleStretchUntil && !settings.Sleep)
                {
                    float stretch = 1 + (float)Math.Sin(now * 8) * 0.055f;
                    reactionScale *= stretch;
                    bob += (stretch - 1) * 30;
                }
                if (DesktopEatingActive)
                {
                    jump = 0;
                    bob = (float)Math.Sin(deleteStageTime * (deleteStage == 2 ? 22 : 18)) * (deleteStage == 2 ? 1.5f : 5f);
                    angle = (float)Math.Sin(deleteStageTime * 16) * (deleteStage == 2 ? 2f : 5f);
                }
                if (ricePose)
                {
                    // Use the same lightweight visual language as petting and jumping:
                    // a quick entrance bounce, a happy wiggle, gentle breathing, and hearts.
                    double elapsed = Math.Max(0, RicePoseDuration - (poseUntil - now));
                    double intro = Math.Min(1, elapsed / 0.42);
                    double eased = 1 - Math.Pow(1 - intro, 3);
                    reactionScale = (float)(0.92 + 0.08 * eased);
                    jump = elapsed < 0.72 ? (float)Math.Sin(elapsed / 0.72 * Math.PI) * 13 : 0;
                    bob = elapsed >= 0.55 ? (float)Math.Sin((elapsed - 0.55) * 3.8) * 1.8f : 0;
                    angle = elapsed < 1.15 ? (float)Math.Sin(elapsed * 15) * 3.2f
                        : (float)Math.Sin((elapsed - 1.15) * 2.5) * 0.8f;
                }
                float spinProgress = 0;
                if (spinning)
                {
                    spinProgress = (float)Math.Max(0, Math.Min(1, (now - spinStartedAt) / (spinUntil - spinStartedAt)));
                    // Cubic easing starts with a tiny anticipation and settles
                    // softly.  The small wobble adds spring without changing the
                    // promised three-to-five full rotations.
                    float eased = 1 - (float)Math.Pow(1 - spinProgress, 3);
                    float wobble = (float)Math.Sin(spinProgress * Math.PI * 12) * (1 - spinProgress) * 6.5f;
                    angle += spinDirection * spinTurns * 360f * eased + wobble;
                    reactionScale *= 1 + (float)Math.Sin(spinProgress * Math.PI * 5) * 0.055f + (1 - spinProgress) * 0.025f;
                    bob += (float)Math.Sin(spinProgress * Math.PI * 8) * 4.5f;
                }
                Rectangle src = crop;
                Bitmap body = art;
                bool showPose = false;
                bool dshAnimation = !DesktopEatingActive && IsDshAnimationActive(now);
                Bitmap dshFrame = null;
                Rectangle dshFrameCrop = Rectangle.Empty;
                bool showDshClip = dshAnimation && TryGetDshAnimation(now, out dshFrame, out dshFrameCrop);
                if (!DesktopEatingActive && settings.Sleep && sleepArt != null) { body = sleepArt; src = sleepCrop; showPose = true; }
                else if (!DesktopEatingActive && poseName == "flower" && flowerArt != null) { body = flowerArt; src = flowerCrop; showPose = true; }
                else if (ricePose && riceArt != null) { body = riceArt; src = riceCrop; showPose = true; }
                int currentView = DesktopEatingActive ? ViewFront : viewDirection;
                int currentSide = sideFacing;
                if (!showPose) GetMovementSprite(currentView, out body, out src);
                float dshScaleX = 1, dshScaleY = 1, dshSwayX = 0, dshBob = 0, dshAngle = 0;
                if (dshAnimation && !showPose && !showDshClip)
                {
                    // A damaged/missing GIF falls back to a small original-art motion.
                    GetDshOriginalArtTransform(now, out dshScaleX, out dshScaleY, out dshSwayX, out dshBob, out dshAngle);
                    bob += dshBob;
                    angle += dshAngle;
                }
                float h, w;
                if (showPose) { float poseScale = Math.Min(252f / src.Width, (ricePose ? 222f : 240f) / src.Height); w = src.Width * poseScale; h = src.Height * poseScale; }
                else { h = 222; w = h * src.Width / src.Height; }
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(25, 51, 83, 139))) g.FillEllipse(shadow, 78 + jump / 4, 317, 108 - jump / 2, 10);
                if (spinning) DrawSpinOrbit(g, now, spinProgress);
                GraphicsState pose = g.Save();
                g.TranslateTransform(132 + dshSwayX, 312 + bob - jump);
                g.RotateTransform(angle);
                g.ScaleTransform(reactionScale * dshScaleX, reactionScale * dshScaleY);
                if (showDshClip && !showPose)
                {
                    // These clips are the same approved blue maid character used by
                    // the DSH pet. Cross-fade for a short moment so entering or
                    // leaving a random action never looks like a hard sprite swap.
                    float clipOpacity = GetDshAnimationOpacity(now);
                    DrawCharacterSprite(g, body, src, h, currentView == ViewSide && currentSide < 0, 1 - clipOpacity);
                    float clipHeight = 232;
                    DrawCharacterSprite(g, dshFrame, dshFrameCrop, clipHeight, false, clipOpacity);
                }
                else if (!showPose && !DesktopEatingActive && now < turnUntil)
                {
                    double progress = Math.Max(0, Math.Min(1, 1 - (turnUntil - now) / 0.26));
                    Bitmap previousBody; Rectangle previousSource;
                    GetMovementSprite(previousViewDirection, out previousBody, out previousSource);
                    DrawCharacterSprite(g, previousBody, previousSource, h, previousViewDirection == ViewSide && previousSideFacing < 0, (float)(1 - progress));
                    DrawCharacterSprite(g, body, src, h, currentView == ViewSide && currentSide < 0, (float)progress);
                }
                else
                {
                    DrawCharacterSprite(g, body, src, h, currentView == ViewSide && currentSide < 0, 1);
                }
                if (dshAnimation && !showPose && !showDshClip) DrawDshOriginalArtEffects(g, now);
                if (deleteStage == 2) DrawEatingMouth(g);
                g.Restore(pose);
                DrawName(g, DesktopEatingActive ? (deleteStage == 2 ? "小deepseek  ·  啊呜！" : "小deepseek  ·  哒哒哒") : ricePose ? "小deepseek  ·  吃白饭" : settings.Sleep ? "小deepseek  ·  休息中" : "小deepseek");
                if (!DesktopEatingActive && settings.Sleep && !showPose)
                {
                    using (Font font = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(190, 87, 134, 221)))
                        g.DrawString("z Z", font, brush, 207, 108 + (float)Math.Sin(now * 1.4) * 5);
                }
                foreach (Particle p in hearts) DrawHeart(g, p);
                if (now < speechUntil) DrawSpeech(g, speech, todoSpeech);
                Native.Present(this, frame);
            }
        }

        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath(); float d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }

        private static void DrawSpeech(Graphics g, string text, bool isTodo)
        {
            float bubbleHeight = isTodo ? 91 : 75;
            RectangleF box = new RectangleF(10, 1, 244, bubbleHeight);
            using (GraphicsPath path = RoundRect(new RectangleF(10, 4, 244, bubbleHeight), 15))
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(20, 40, 65, 110))) g.FillPath(shadow, path);
            using (GraphicsPath path = RoundRect(box, 15))
            using (SolidBrush white = new SolidBrush(Color.FromArgb(250, 252, 254, 255)))
            using (Pen border = new Pen(Color.FromArgb(210, 166, 199, 244), 1.3f))
            {
                g.FillPath(white, path); g.DrawPath(border, path);
                g.FillPolygon(white, new PointF[] { new PointF(121, bubbleHeight), new PointF(132, bubbleHeight + 10), new PointF(143, bubbleHeight) });
                g.DrawLines(border, new PointF[] { new PointF(121, bubbleHeight + 1), new PointF(132, bubbleHeight + 10), new PointF(143, bubbleHeight + 1) });
            }
            using (Font font = new Font("Microsoft YaHei UI", isTodo ? 14f : 16f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(Color.FromArgb(51, 74, 112)))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, font, ink, new RectangleF(19, 5, 226, bubbleHeight - 9), format);
        }

        private static void DrawName(Graphics g, string text)
        {
            using (Font font = new Font("Microsoft YaHei UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                float width = g.MeasureString(text, font).Width + 22;
                RectangleF rect = new RectangleF(132 - width / 2, 324, width, 21);
                using (GraphicsPath pill = RoundRect(rect, 10))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(231, 244, 249, 255)))
                using (SolidBrush ink = new SolidBrush(Color.FromArgb(62, 96, 152)))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                { g.FillPath(fill, pill); g.DrawString(text, font, ink, rect, format); }
            }
        }

        private static void DrawHeart(Graphics g, Particle p)
        {
            float x = p.X, y = p.Y, s = 6;
            using (GraphicsPath path = new GraphicsPath())
            using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(Math.Min(1, p.Life) * 225), 246, 120, 170)))
            {
                path.AddBezier(x, y + s, x - s * 2, y - s / 2, x - s, y - s * 1.5f, x, y - s / 3);
                path.AddBezier(x, y - s / 3, x + s, y - s * 1.5f, x + s * 2, y - s / 2, x, y + s);
                g.FillPath(brush, path);
            }
        }

        private void DrawSpinOrbit(Graphics g, double now, float progress)
        {
            float fade = Math.Min(1f, progress * 5) * Math.Min(1f, (1 - progress) * 7 + 0.3f);
            float phase = (float)(now * 14 * spinDirection);
            using (Pen arc = new Pen(Color.FromArgb((int)(fade * 118), 105, 176, 246), 2.3f))
            using (SolidBrush blue = new SolidBrush(Color.FromArgb((int)(fade * 205), 111, 187, 255)))
            using (SolidBrush pink = new SolidBrush(Color.FromArgb((int)(fade * 205), 248, 140, 191)))
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = phase + i * (float)(Math.PI * 2 / 3);
                    float radiusX = 89 + (float)Math.Sin(progress * Math.PI + i) * 7;
                    float radiusY = 57 + (float)Math.Cos(progress * Math.PI + i) * 5;
                    float x = 132 + (float)Math.Cos(a) * radiusX;
                    float y = 202 + (float)Math.Sin(a) * radiusY;
                    g.FillEllipse(i % 2 == 0 ? blue : pink, x - 4.5f, y - 4.5f, 9, 9);
                }
                g.DrawArc(arc, 39, 143, 186, 118, (float)(phase * 57.3), 116);
                g.DrawArc(arc, 50, 151, 164, 102, (float)(phase * 57.3 + 180), 92);
            }
        }

        private void BringPetBack()
        {
            Location = ClampToScreen(Location);
            if (!Visible) Show();
            TopMost = settings.Top;
            if (settings.StatusBoardVisible) ShowStatusBoard();
            BringToFront();
            lastTick = watch.Elapsed.TotalSeconds; timer.Start();
            Say("我回来啦！一直都在哦。", 4); RenderFrame();
        }

        private void DisplayChanged(object sender, EventArgs e)
        {
            if (closing || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { CancelDesktopEating(); Location = ClampToScreen(Location); Save(); RenderFrame(); }); }
            catch (InvalidOperationException) { }
        }

        protected override void WndProc(ref Message m)
        {
            if (desktopMonitor != null && desktopMonitor.ProcessMessage(ref m)) return;
            if (m.Msg == Program.ShowMessage) { BringPetBack(); return; }
            base.WndProc(ref m);
        }

        private void Save() { settings.Save(DesktopEatingActive ? deleteHome : Location); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true; timer.Stop(); clickTimer.Stop(); Save(); StopDesktopMonitoring(); CancelDesktopEating();
            StopTodoReminders();
            if (todoWindow != null && !todoWindow.IsDisposed) todoWindow.Dispose();
            if (statusBoard != null && !statusBoard.IsDisposed) statusBoard.Shutdown();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose(); clickTimer.Dispose();
                if (interactionVoice != null) { try { interactionVoice.SpeakAsyncCancelAll(); interactionVoice.Dispose(); } catch { } interactionVoice = null; }
                DisposeDshAnimations();
                StopTodoReminders();
                StopDesktopMonitoring(); CancelDesktopEating();
                if (todoWindow != null) { todoWindow.Dispose(); todoWindow = null; }
                if (statusBoard != null) { statusBoard.Dispose(); statusBoard = null; }
                if (tray != null) tray.Dispose();
                menu.Dispose(); art.Dispose(); petIcon.Dispose();
                if (sideArt != null) sideArt.Dispose();
                if (backArt != null) backArt.Dispose();
                if (sleepArt != null) sleepArt.Dispose();
                if (flowerArt != null) flowerArt.Dispose();
                if (riceArt != null) riceArt.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; internal Point(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Size { internal int W, H; internal Size(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { internal byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr screen, ref Point target, ref Size size, IntPtr source, ref Point origin, uint key, ref Blend blend, uint flags);

        internal static void Present(Form form, Bitmap bitmap)
        {
            IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, handle = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                memory = CreateCompatibleDC(screen);
                handle = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory, handle);
                Point target = new Point(form.Left, form.Top), origin = new Point(0, 0);
                Size size = new Size(bitmap.Width, bitmap.Height);
                Blend blend = new Blend { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(form.Handle, screen, ref target, ref size, memory, ref origin, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memory, previous);
                if (handle != IntPtr.Zero) DeleteObject(handle);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
