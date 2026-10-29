using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LittleDeepSeek
{
    internal sealed partial class TodoForm : Form
    {
        private const string TodoHost = "todo.littledeepseek.example";
        private const string MessageType = "littledeepseek-todos-v1";
        private readonly string dataFolder;
        private readonly WebView2 browser;
        private readonly Label loading;
        private readonly Timer snapshotTimeout = new Timer { Interval = 15000 };
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        private bool shuttingDown, loaded, focusRequested, coreConfigured, scriptInstalled, navigationPending, snapshotRequestInFlight;
        private Task startingTask;

        // Raised on the WinForms UI thread. The snapshot is the authoritative stored
        // JSON array; task field/date validation belongs to the reminder service.
        internal event Action<string> TasksSnapshotReceived;
        internal event Action<string> SnapshotFailed;
        internal bool NeedsRecreation { get; private set; }
        // A background reader has a native handle but has never been presented.
        // Recreate that reader before a user-facing open so WinForms can create a
        // normal visible top-level window on every supported Windows build.
        internal bool HasBeenShown { get; private set; }
        partial void InitializeVoicePanel();
        partial void DisposeVoicePanel();

        internal TodoForm(string dataFolder, Icon icon)
        {
            this.dataFolder = dataFolder;
            Text = "todo-list · 小deepseek";
            AccessibleName = "小deepseek待办清单";
            Icon = icon;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(640, 520);
            Size = new Size(Math.Min(1160, Screen.PrimaryScreen.WorkingArea.Width - 80),
                Math.Min(850, Screen.PrimaryScreen.WorkingArea.Height - 80));
            BackColor = Color.FromArgb(246, 248, 251);
            loading = new Label {
                Text = "正在打开待办清单…", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 12f),
                ForeColor = Color.FromArgb(66, 99, 235)
            };
            browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
            Controls.Add(browser);
            Controls.Add(loading);
            loading.BringToFront();
            snapshotTimeout.Tick += delegate { ReportSnapshotFailure("读取待办清单超时，稍后会自动重试。"); };
            InitializeVoicePanel();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            HasBeenShown = true;
            StartBackground();
        }

        // Creating hidden HWNDs lets WebView2 read the existing profile without
        // displaying or activating the todo window when the pet starts.
        internal void StartBackground()
        {
            RequestSnapshot();
        }

        private Task EnsureStartedAsync()
        {
            if (startingTask != null && !startingTask.IsCompleted) return startingTask;
            if (shuttingDown || IsDisposed || loaded || navigationPending) return Task.FromResult(0);
            startingTask = InitializeAndNavigateAsync();
            return startingTask;
        }

        private async Task InitializeAndNavigateAsync()
        {
            try
            {
                string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "todo-app");
                if (!File.Exists(Path.Combine(assets, "index.html")))
                    throw new FileNotFoundException("找不到待办页面，请保留程序旁的 todo-app 文件夹。");
                Directory.CreateDirectory(dataFolder);
                // Accessing Handle creates a hidden native window; Show is never used here.
                IntPtr formHandle = Handle;
                IntPtr browserHandle = browser.Handle;
                if (browser.CoreWebView2 == null)
                {
                    CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                    if (shuttingDown || IsDisposed) return;
                    await browser.EnsureCoreWebView2Async(environment);
                    if (shuttingDown || IsDisposed) return;
                }
                if (!coreConfigured)
                {
                    ConfigureCore(assets);
                    coreConfigured = true;
                }
                if (!scriptInstalled)
                {
                    await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(SnapshotBridgeScript);
                    if (shuttingDown || IsDisposed) return;
                    scriptInstalled = true;
                }
                loading.Text = "正在打开待办清单…";
                loading.ForeColor = Color.FromArgb(66, 99, 235);
                loading.Visible = true;
                navigationPending = true;
                browser.CoreWebView2.Navigate("https://" + TodoHost + "/index.html");
            }
            catch (Exception error)
            {
                if (!shuttingDown && !IsDisposed)
                {
                    navigationPending = false;
                    ShowFailure("待办清单暂时无法打开。\n" + error.Message +
                        "\n\n请确认 Microsoft Edge WebView2 Runtime 已安装，且 data 文件夹可写。\n关闭窗口后，可从 todo-list 菜单重试。");
                    ReportSnapshotFailure(error.Message);
                    try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "todo-error.log"), error.ToString()); } catch { }
                }
            }
        }

        private void ConfigureCore(string assets)
        {
            CoreWebView2 core = browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(TodoHost, assets, CoreWebView2HostResourceAccessKind.Deny);
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs args) { args.Handled = true; };
            core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs args)
            {
                if (!IsTodoOrigin(args.Uri)) { args.Cancel = true; return; }
                loaded = false;
                navigationPending = true;
            };
            core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                if (shuttingDown || IsDisposed) return;
                navigationPending = false;
                if (!args.IsSuccess)
                {
                    ShowFailure("待办页面加载失败。关闭窗口后，请从 todo-list 菜单重新打开。");
                    ReportSnapshotFailure("待办页面加载失败：" + args.WebErrorStatus);
                    return;
                }
                loaded = true;
                loading.Visible = false;
                RequestSnapshot();
                if (Visible)
                {
                    browser.Focus();
                    if (focusRequested) FocusTaskInput();
                }
            };
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs args)
            {
                if (shuttingDown || IsDisposed) return;
                navigationPending = false;
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                    NeedsRecreation = true;
                ShowFailure("待办页面暂时中断。关闭窗口后，请从 todo-list 菜单重新打开。");
                ReportSnapshotFailure("待办页面进程中断：" + args.ProcessFailedKind);
            };
        }

        private static bool IsTodoOrigin(string address)
        {
            Uri destination;
            return Uri.TryCreate(address, UriKind.Absolute, out destination) &&
                destination.Scheme == Uri.UriSchemeHttps &&
                string.Equals(destination.Host, TodoHost, StringComparison.OrdinalIgnoreCase) &&
                destination.Port == 443 && string.IsNullOrEmpty(destination.UserInfo);
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (shuttingDown || IsDisposed || !IsTodoOrigin(args.Source)) return;
            string snapshot;
            try
            {
                Dictionary<string, object> envelope = json.DeserializeObject(args.WebMessageAsJson) as Dictionary<string, object>;
                object type, error, tasks;
                if (envelope == null || !envelope.TryGetValue("type", out type) || !MessageType.Equals(type)) return;
                snapshotTimeout.Stop();
                if (envelope.TryGetValue("error", out error) && error is string)
                {
                    ReportSnapshotFailure((string)error);
                    return;
                }
                if (!envelope.TryGetValue("tasks", out tasks) || !(tasks is object[]))
                {
                    ReportSnapshotFailure("待办清单数据格式不正确，已暂停本次提醒读取。");
                    return;
                }
                snapshot = json.Serialize(tasks);
            }
            catch (Exception error)
            {
                ReportSnapshotFailure("无法读取待办清单：" + error.Message);
                return;
            }
            Action<string> handler = TasksSnapshotReceived;
            if (handler != null) handler(snapshot);
        }

        internal async void RequestSnapshot()
        {
            if (shuttingDown || IsDisposed || snapshotRequestInFlight) return;
            snapshotRequestInFlight = true;
            snapshotTimeout.Stop();
            snapshotTimeout.Start();
            try
            {
                await EnsureStartedAsync();
                if (shuttingDown || IsDisposed || !loaded || browser.CoreWebView2 == null) return;
                string result = await browser.CoreWebView2.ExecuteScriptAsync(
                    "(function(){if(typeof window.__littleDeepSeekRequestTodoSnapshot!=='function')return false;" +
                    "window.__littleDeepSeekRequestTodoSnapshot();return true;})();");
                if (!shuttingDown && !IsDisposed && result != "true")
                    ReportSnapshotFailure("待办清单连接尚未就绪，稍后会自动重试。");
            }
            catch (Exception error)
            {
                if (!shuttingDown && !IsDisposed) ReportSnapshotFailure("无法刷新待办清单：" + error.Message);
            }
            finally { snapshotRequestInFlight = false; }
        }

        internal void OpenList(bool focusInput)
        {
            if (shuttingDown || IsDisposed) return;
            focusRequested = focusRequested || focusInput;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            StartBackground();
            if (loaded && focusRequested) FocusTaskInput();
        }

        private async void FocusTaskInput()
        {
            if (shuttingDown || IsDisposed || !Visible || !loaded) return;
            focusRequested = false;
            browser.Focus();
            try
            {
                await browser.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var tries=0;function focusTask(){if(document.hidden)return;" +
                    "var input=document.querySelector('input[aria-label=\"新任务内容\"]');" +
                    "if(input&&!input.disabled){input.focus();}else if(++tries<40){setTimeout(focusTask,50);}}focusTask();})();");
            }
            catch (InvalidOperationException) { }
            catch (System.Runtime.InteropServices.COMException) { }
        }

        private void ShowFailure(string message)
        {
            loaded = false;
            loading.Text = message;
            loading.ForeColor = Color.FromArgb(157, 76, 38);
            loading.Visible = true;
            loading.BringToFront();
        }

        private void ReportSnapshotFailure(string message)
        {
            snapshotTimeout.Stop();
            Action<string> handler = SnapshotFailed;
            if (handler != null) handler(message);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!shuttingDown && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                focusRequested = false;
                Hide();
            }
            else shuttingDown = true;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { shuttingDown = true; snapshotTimeout.Dispose(); DisposeVoicePanel(); browser.Dispose(); loading.Font.Dispose(); }
            base.Dispose(disposing);
        }

        // The page already gates its first save until localStorage has loaded.
        // This bridge only observes storage; it never writes or migrates user tasks.
        private const string SnapshotBridgeScript = @"
(function () {
  'use strict';
  if (window.top !== window || location.origin !== 'https://todo.littledeepseek.example') return;
  if (window.__littleDeepSeekRequestTodoSnapshot) return;
  var key = 'yijian-todos-v1', type = 'littledeepseek-todos-v1';
  var store, lastRaw, hasSnapshot = false, lastError = null;
  function fail(error, force) {
    var message = '无法读取待办清单：' + (error && error.message ? error.message : String(error));
    if (force || message !== lastError) {
      lastError = message;
      window.chrome.webview.postMessage({ type: type, error: message });
    }
  }
  function snapshot(force) {
    try {
      store = window.localStorage;
      var raw = store.getItem(key);
      if (!force && hasSnapshot && raw === lastRaw && lastError === null) return;
      var tasks = raw === null ? [] : JSON.parse(raw);
      if (!Array.isArray(tasks)) throw new Error('已保存的任务不是数组');
      window.chrome.webview.postMessage({ type: type, tasks: tasks });
      lastRaw = raw;
      hasSnapshot = true;
      lastError = null;
    } catch (error) { fail(error, force); }
  }
  Object.defineProperty(window, '__littleDeepSeekRequestTodoSnapshot', {
    value: function () { snapshot(true); }, configurable: false, writable: false
  });
  ['setItem', 'removeItem', 'clear'].forEach(function (name) {
    var original = Storage.prototype[name];
    Storage.prototype[name] = function () {
      var result = original.apply(this, arguments);
      if (this === store && (name === 'clear' || String(arguments[0]) === key)) snapshot(false);
      return result;
    };
  });
  window.addEventListener('storage', function (event) {
    if (event.storageArea === store && (event.key === key || event.key === null)) snapshot(false);
  });
  window.addEventListener('pageshow', function () { snapshot(true); });
  document.addEventListener('DOMContentLoaded', function () { snapshot(true); });
  snapshot(true);
  setInterval(function () { snapshot(false); }, 2000);
})();";
    }
}
