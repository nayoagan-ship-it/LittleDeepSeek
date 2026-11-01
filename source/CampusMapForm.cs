using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LittleDeepSeek
{
    internal static class CampusMapService
    {
        internal static string Url
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("LITTLEDEEPSEEK_MAP_URL");
                if (String.IsNullOrWhiteSpace(configured)) return "https://littledeepseek-campus.nayoagan.chatgpt.site/";
                Uri address;
                if (!Uri.TryCreate(configured, UriKind.Absolute, out address) ||
                    (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidOperationException("LITTLEDEEPSEEK_MAP_URL 必须是 HTTP 或 HTTPS 地址。");
                return address.AbsoluteUri;
            }
        }
        internal static string ProjectFolder
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("LITTLEDEEPSEEK_MAP_DIRECTORY");
                return String.IsNullOrWhiteSpace(configured) ? null : Path.GetFullPath(configured);
            }
        }

        internal static bool IsReady()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
                request.Proxy = null;
                request.UserAgent = "LittleDeepSeek/0.2";
                request.Timeout = request.ReadWriteTimeout = 8000;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return response.StatusCode == HttpStatusCode.OK && reader.ReadToEnd().Contains("南京农业大学地图导航");
            }
            catch (WebException) { return false; }
            catch (IOException) { return false; }
        }

        internal static Task EnsureRunningAsync()
        {
            return Task.Run(async delegate
            {
                if (IsReady()) return;
                string folder = ProjectFolder;
                if (folder == null) throw new InvalidOperationException("暂时无法连接公共校园地图，请检查网络后重试。");
                string launcher = Path.Combine(folder, "start-local.ps1");
                if (!File.Exists(launcher)) throw new FileNotFoundException("请先单独部署校园地图，或设置 LITTLEDEEPSEEK_MAP_URL / LITTLEDEEPSEEK_MAP_DIRECTORY。");
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                ProcessStartInfo start = new ProcessStartInfo(powershell,
                    "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + launcher + "\" -NoBrowser")
                {
                    WorkingDirectory = folder, UseShellExecute = false,
                    CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(start))
                {
                    Stopwatch elapsed = Stopwatch.StartNew();
                    while (elapsed.Elapsed.TotalSeconds < 70)
                    {
                        if (IsReady()) return;
                        if (process.HasExited && process.ExitCode != 0)
                            throw new InvalidOperationException("校园地图服务未能启动，请稍后重试。可用项目的 start-local.bat 检查启动情况。");
                        await Task.Delay(700).ConfigureAwait(false);
                    }
                }
                throw new TimeoutException("校园地图启动时间较长，请稍后点击“重新加载”。");
            });
        }
    }

    internal sealed class CampusMapForm : Form
    {
        private readonly WebView2 browser;
        private readonly Label notice;
        private readonly Button reload, external;
        private readonly string profileFolder;
        private bool busy, closing;

        internal CampusMapForm(string profileFolder, Icon icon, PetIdentity identity)
        {
            this.profileFolder = profileFolder;
            identity.Bind(this, "{pet} · 校园地图", "{pet}校园地图");
            Icon = icon;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(850, 620);
            Size = new Size(Math.Min(1320, Screen.PrimaryScreen.WorkingArea.Width - 80),
                Math.Min(920, Screen.PrimaryScreen.WorkingArea.Height - 80));
            BackColor = Color.FromArgb(247, 250, 253);
            Font = new Font("Microsoft YaHei UI", 9f);

            Panel toolbar = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(12, 10, 12, 10), BackColor = Color.FromArgb(237, 245, 253) };
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 265, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            reload = new Button { Text = "重新加载", AutoSize = true, Height = 30 };
            external = new Button { Text = "在浏览器中打开", AutoSize = true, Height = 30, Enabled = false };
            reload.Click += async delegate { await LoadMapAsync(); };
            external.Click += delegate { Process.Start(CampusMapService.Url); };
            actions.Controls.Add(reload); actions.Controls.Add(external);
            Label heading = new Label { Text = "校园地图  ·  高德地图 / DeepSeek 智能路线", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(46, 82, 125) };
            toolbar.Controls.Add(heading); toolbar.Controls.Add(actions);

            Panel content = new Panel { Dock = DockStyle.Fill };
            browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
            notice = new Label { Text = "正在准备校园地图…", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(30), Font = new Font("Microsoft YaHei UI", 12f), ForeColor = Color.FromArgb(66, 99, 160), BackColor = BackColor };
            content.Controls.Add(browser); content.Controls.Add(notice);
            Controls.Add(content); Controls.Add(toolbar);
            notice.BringToFront();
            browser.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (closing || IsDisposed) return;
                if (e.IsSuccess) { notice.Visible = false; browser.Visible = true; }
                else ShowNotice("地图页面未能加载，请点击“重新加载”重试。");
            };
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await LoadMapAsync();
        }

        private void ShowNotice(string message)
        {
            notice.Text = message; notice.Visible = true; notice.BringToFront();
        }

        private async Task LoadMapAsync()
        {
            if (busy || closing || IsDisposed) return;
            busy = true; reload.Enabled = false; external.Enabled = false;
            ShowNotice("正在连接校园地图…\n首次启动可能需要稍等片刻。");
            try
            {
                await CampusMapService.EnsureRunningAsync();
                if (closing || IsDisposed) return;
                if (browser.CoreWebView2 == null)
                {
                    Directory.CreateDirectory(profileFolder);
                    CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, profileFolder);
                    if (closing || IsDisposed) return;
                    await browser.EnsureCoreWebView2Async(environment);
                }
                if (closing || IsDisposed) return;
                browser.CoreWebView2.Navigate(CampusMapService.Url);
                external.Enabled = true;
            }
            catch (Exception error)
            {
                if (!closing && !IsDisposed) ShowNotice("校园地图暂时没有打开。\n" + error.Message + "\n\n点击“重新加载”可以再次尝试。");
                Trace.WriteLine("Campus map: " + error.Message);
            }
            finally
            {
                busy = false;
                if (!closing && !IsDisposed) reload.Enabled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { closing = true; browser.Dispose(); notice.Font.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed partial class PetForm
    {
        private CampusMapForm campusMapWindow;
        private void OpenCampusMap()
        {
            if (campusMapWindow == null || campusMapWindow.IsDisposed)
            {
                campusMapWindow = new CampusMapForm(Path.Combine(Path.GetDirectoryName(settings.PathName), "campus-map-webview"), petIcon, identity);
                campusMapWindow.Show();
            }
            else
            {
                if (campusMapWindow.WindowState == FormWindowState.Minimized) campusMapWindow.WindowState = FormWindowState.Normal;
                campusMapWindow.Show();
            }
            campusMapWindow.Activate();
        }
    }
}
