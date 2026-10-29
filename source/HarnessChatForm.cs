using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    // Uses the locally installed DeepSeek Harness headless profile.  The profile
    // owns the model, tools, and permission policy; this window only collects a
    // request and presents its streamed progress and final answer.
    internal sealed class HarnessChatForm : Form
    {
        private readonly TextBox requestBox;
        private readonly TextBox resultBox;
        private readonly Button submitButton;
        private readonly Button stopButton;
        private readonly Label statusLabel;
        private Process runningProcess;
        private bool closing;

        internal HarnessChatForm(Icon icon)
        {
            Text = "小deepseek · deepseek求索";
            Icon = icon;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(650, 540);
            Size = new Size(760, 640);
            BackColor = Color.FromArgb(246, 249, 253);
            Font = new Font("Microsoft YaHei UI", 9.5f);

            Panel header = new Panel { Dock = DockStyle.Top, Height = 122 };
            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(header.ClientRectangle, Color.FromArgb(34, 104, 224), Color.FromArgb(84, 68, 190), LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(brush, header.ClientRectangle);
                using (SolidBrush bubble = new SolidBrush(Color.FromArgb(50, Color.White))) e.Graphics.FillEllipse(bubble, header.Width - 120, -42, 162, 162);
                using (SolidBrush bubble = new SolidBrush(Color.FromArgb(34, Color.White))) e.Graphics.FillEllipse(bubble, header.Width - 220, 71, 72, 72);
            };
            Label badge = new Label { Text = "DS", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(39, 92, 198), BackColor = Color.White, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Location = new Point(22, 28), Size = new Size(54, 54) };
            badge.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen outline = new Pen(Color.FromArgb(210, 225, 255), 1.5f)) e.Graphics.DrawEllipse(outline, 1, 1, badge.Width - 3, badge.Height - 3);
            };
            Label title = new Label { Text = "deepseek 求索", AutoSize = true, BackColor = Color.Transparent, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold), Location = new Point(91, 25) };
            Label subtitle = new Label { Text = "小deepseek 的智能工作台 · 可选接入 DeepSeek Harness", AutoSize = true, BackColor = Color.Transparent, ForeColor = Color.FromArgb(225, 237, 255), Font = new Font("Microsoft YaHei UI", 9.5f), Location = new Point(94, 62) };
            Label headerHint = new Label { Text = "把想做的事交给我", AutoSize = true, BackColor = Color.Transparent, ForeColor = Color.FromArgb(218, 232, 255), Font = new Font("Microsoft YaHei UI", 9f), Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(590, 90) };
            header.Controls.Add(badge);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(headerHint);

            Panel requestCard = CreateCard(new Point(18, 140), new Size(724, 170), AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
            Label requestLabel = new Label { Text = "这次想完成什么？", AutoSize = true, ForeColor = Color.FromArgb(30, 45, 70), Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), Location = new Point(18, 15) };
            Label requestHint = new Label { Text = "说明目标、文件或期望结果，小deepseek 会直接交给 Harness 处理。", AutoSize = true, ForeColor = Color.FromArgb(113, 131, 158), Location = new Point(18, 43) };
            Panel requestSurface = CreateInputSurface(new Point(18, 68), new Size(688, 52), AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
            requestBox = new TextBox
            {
                AcceptsReturn = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(37, 52, 77),
                Font = new Font("Microsoft YaHei UI", 10f),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(11, 10),
                Size = new Size(664, 32)
            };
            requestSurface.Controls.Add(requestBox);
            submitButton = CreateActionButton("开始求索  →", Color.FromArgb(37, 111, 235), Color.FromArgb(24, 92, 210));
            submitButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            submitButton.Location = new Point(554, 130);
            stopButton = CreateActionButton("停止", Color.FromArgb(239, 244, 251), Color.FromArgb(224, 232, 244));
            stopButton.ForeColor = Color.FromArgb(85, 106, 137);
            stopButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            stopButton.Location = new Point(471, 130);
            stopButton.Size = new Size(72, 28);
            stopButton.Enabled = false;
            Label examples = new Label { Text = "试试：检查项目问题  ·  整理一份方案  ·  分析报错原因", AutoSize = true, ForeColor = Color.FromArgb(132, 147, 170), Font = new Font("Microsoft YaHei UI", 8.5f), Location = new Point(18, 136) };
            requestCard.Controls.Add(requestLabel);
            requestCard.Controls.Add(requestHint);
            requestCard.Controls.Add(requestSurface);
            requestCard.Controls.Add(examples);
            requestCard.Controls.Add(stopButton);
            requestCard.Controls.Add(submitButton);

            Panel resultCard = CreateCard(new Point(18, 326), new Size(724, 274), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            Label resultLabel = new Label { Text = "求索记录", AutoSize = true, ForeColor = Color.FromArgb(30, 45, 70), Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), Location = new Point(18, 15) };
            statusLabel = new Label { Text = "●  准备就绪，等你交代任务", AutoSize = false, ForeColor = Color.FromArgb(108, 129, 158), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Location = new Point(18, 44), Size = new Size(688, 22) };
            Panel resultSurface = CreateInputSurface(new Point(18, 74), new Size(688, 181), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            resultBox = new TextBox
            {
                ReadOnly = true,
                Multiline = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = new Font("Microsoft YaHei UI", 9.5f),
                ForeColor = Color.FromArgb(49, 65, 91),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(11, 10),
                Size = new Size(664, 159),
                BackColor = Color.White
            };
            resultSurface.Controls.Add(resultBox);
            resultCard.Controls.Add(resultLabel);
            resultCard.Controls.Add(statusLabel);
            resultCard.Controls.Add(resultSurface);
            Controls.Add(header);
            Controls.Add(requestCard);
            Controls.Add(resultCard);
            AcceptButton = submitButton;
            submitButton.Click += delegate { StartHarnessTask(); };
            stopButton.Click += delegate { StopHarnessTask(false); };
            FormClosing += delegate { closing = true; StopHarnessTask(true); };
            Shown += delegate { requestBox.Focus(); };
        }

        private void StartHarnessTask()
        {
            string request = requestBox.Text.Trim();
            if (request.Length == 0)
            {
                MessageBox.Show(this, "先写下想让小deepseek做的事情吧。", "deepseek求索", MessageBoxButtons.OK, MessageBoxIcon.Information);
                requestBox.Focus();
                return;
            }

            string node = FindNodeExecutable();
            string harness = FindHarnessEntry();
            if (node == null || harness == null)
            {
                MessageBox.Show(this, "没有找到可用的 DeepSeek Harness。请先启动或安装 Harness，再试一次。", "deepseek求索", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            resultBox.Clear();
            AppendLine("小deepseek 正在把需求交给 DeepSeek Harness…");
            SetStatus("●  DeepSeek Harness 正在工作，请稍候…", Color.FromArgb(37, 111, 235));
            submitButton.Enabled = false;
            stopButton.Enabled = true;
            requestBox.Enabled = false;

            string task = "请直接处理以下用户需求；可以使用你已配置的工具完成工作，并在结束时给出清晰的结果：\n" + request;
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = node,
                Arguments = Quote(harness) + " --profile headless " + Quote(task),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = FindHarnessWorkspace(),
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            if (String.IsNullOrWhiteSpace(start.EnvironmentVariables["DSH_HOME"]))
                start.EnvironmentVariables["DSH_HOME"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");

            try
            {
                runningProcess = new Process { StartInfo = start, EnableRaisingEvents = true };
                runningProcess.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) AppendLine(e.Data); };
                runningProcess.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) AppendLine(e.Data); };
                runningProcess.Exited += delegate { HarnessExited(); };
                runningProcess.Start();
                runningProcess.BeginOutputReadLine();
                runningProcess.BeginErrorReadLine();
            }
            catch (Exception error)
            {
                AppendLine("启动 Harness 失败：" + error.Message);
                FinishTask(false, "未能启动 DeepSeek Harness。");
            }
        }

        private void HarnessExited()
        {
            Process completed = runningProcess;
            int exitCode = -1;
            try { if (completed != null) exitCode = completed.ExitCode; } catch { }
            FinishTask(exitCode == 0, exitCode == 0 ? "任务已完成。" : "Harness 已结束（退出代码 " + exitCode + "）。");
            try { if (completed != null) completed.Dispose(); } catch { }
        }

        private void FinishTask(bool succeeded, string message)
        {
            if (closing || IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<bool, string>(FinishTask), succeeded, message); return; }
            runningProcess = null;
            SetStatus("●  " + message, succeeded ? Color.FromArgb(32, 141, 98) : Color.FromArgb(202, 87, 86));
            submitButton.Enabled = true;
            stopButton.Enabled = false;
            requestBox.Enabled = true;
            if (!succeeded) AppendLine("\r\n小deepseek：可以检查 Harness 是否已正常启动后重试。");
        }

        private void StopHarnessTask(bool silent)
        {
            Process process = runningProcess;
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); } catch { }
            if (!silent) SetStatus("●  已请求停止 Harness 任务。", Color.FromArgb(108, 129, 158));
        }

        private void AppendLine(string text)
        {
            if (closing || IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLine), text); return; }
            resultBox.AppendText(text + Environment.NewLine);
            resultBox.SelectionStart = resultBox.TextLength;
            resultBox.ScrollToCaret();
        }

        private void SetStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }

        private static Panel CreateCard(Point location, Size size, AnchorStyles anchor)
        {
            Panel card = new Panel { BackColor = Color.White, Location = location, Size = size, Anchor = anchor };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen border = new Pen(Color.FromArgb(223, 232, 245))) e.Graphics.DrawRectangle(border, 0, 0, card.Width - 1, card.Height - 1);
            };
            return card;
        }

        private static Panel CreateInputSurface(Point location, Size size, AnchorStyles anchor)
        {
            Panel surface = new Panel { BackColor = Color.White, Location = location, Size = size, Anchor = anchor };
            surface.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen border = new Pen(Color.FromArgb(207, 220, 238))) e.Graphics.DrawRectangle(border, 0, 0, surface.Width - 1, surface.Height - 1);
            };
            return surface;
        }

        private static Button CreateActionButton(string text, Color backColor, Color hoverColor)
        {
            Button button = new Button
            {
                Text = text,
                Size = new Size(152, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = hoverColor;
            button.FlatAppearance.MouseDownBackColor = hoverColor;
            return button;
        }

        private static string FindNodeExecutable()
        {
            string configured = Environment.GetEnvironmentVariable("LITTLEDEEPSEEK_NODE");
            if (!String.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe")
            };
            return candidates.FirstOrDefault(File.Exists) ?? "node";
        }

        private static string FindHarnessEntry()
        {
            string configured = Environment.GetEnvironmentVariable("LITTLEDEEPSEEK_HARNESS_ENTRY");
            if (!String.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
            string globalEntry = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
            if (File.Exists(globalEntry)) return globalEntry;
            string npmCache = Environment.GetEnvironmentVariable("NPM_CONFIG_CACHE");
            if (String.IsNullOrWhiteSpace(npmCache)) npmCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm-cache");
            string cache = Path.Combine(npmCache, "_npx");
            try
            {
                return Directory.GetDirectories(cache)
                    .Select(folder => Path.Combine(folder, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static string FindHarnessWorkspace()
        {
            string workspace = Environment.GetEnvironmentVariable("LITTLEDEEPSEEK_WORKSPACE");
            return Directory.Exists(workspace) ? workspace : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static string Quote(string value)
        {
            StringBuilder quoted = new StringBuilder();
            quoted.Append('"');
            int slashes = 0;
            foreach (char character in value ?? "")
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') quoted.Append('\\', slashes * 2 + 1);
                else quoted.Append('\\', slashes);
                quoted.Append(character);
                slashes = 0;
            }
            quoted.Append('\\', slashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }
    }
}
