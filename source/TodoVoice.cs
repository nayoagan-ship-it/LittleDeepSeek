using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    internal sealed partial class TodoForm
    {
        private Panel voicePanel;
        private RichTextBox voiceConversation;
        private TextBox voiceText;
        private Label voiceStatus;
        private Button voiceStart, voiceStop, voiceExecute;
        private VoiceInputSession voiceSession;
        private bool voiceBusy, voiceDisposed;
        private int voiceEpoch;
        private readonly JavaScriptSerializer voiceJson = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };

        private sealed class VoiceSnapshot
        {
            public List<VoiceTodoItem> tasks { get; set; }
            public string storageError { get; set; }
        }
        private sealed class VoiceApplyResult
        {
            public bool ok { get; set; }
            public string error { get; set; }
        }

        partial void InitializeVoicePanel()
        {
            voicePanel = new Panel { Dock = DockStyle.Bottom, Height = 278, Visible = false,
                BackColor = Color.FromArgb(236, 243, 255), Padding = new Padding(14, 8, 14, 8) };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.Controls.Add(new Label { Text = "和小deepseek说一说", Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(42, 75, 148) }, 0, 0);
            layout.Controls.Add(new Label { Text = "试试：添加明天买牛奶  /  买牛奶已经完成", Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9f), ForeColor = Color.FromArgb(68, 81, 107) }, 0, 1);
            voiceConversation = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true,
                BackColor = Color.White, BorderStyle = BorderStyle.None, DetectUrls = false,
                Font = new Font("Microsoft YaHei UI", 9.5f), AccessibleName = "语音待办对话记录" };
            layout.Controls.Add(voiceConversation, 0, 2);
            voiceStatus = new Label { Text = "麦克风未开启", Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9f),
                AccessibleName = "语音输入状态" };
            layout.Controls.Add(voiceStatus, 0, 3);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            voiceStart = MakeVoiceButton("开启语音输入", delegate { StartVoiceInput(); });
            voiceStop = MakeVoiceButton("停止语音", delegate { StopVoiceInput(true); });
            voiceStop.Enabled = false;
            buttons.Controls.Add(voiceStart);
            buttons.Controls.Add(voiceStop);
            buttons.Controls.Add(MakeVoiceButton("暂不开启", delegate {
                StopVoiceInput(false); AppendVoice("小deepseek", "好的，麦克风未开启。也可以在下方输入待办指令。");
                voiceText.Focus();
            }));
            buttons.Controls.Add(MakeVoiceButton("收起对话", delegate { StopVoiceInput(false); voicePanel.Visible = false; }));
            layout.Controls.Add(buttons, 0, 4);
            TableLayoutPanel inputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            voiceText = new TextBox { Dock = DockStyle.Fill, MaxLength = 1500,
                Font = new Font("Microsoft YaHei UI", 10f), AccessibleName = "输入或修正语音待办指令" };
            voiceExecute = MakeVoiceButton("执行", delegate { SubmitVoiceText(); });
            voiceExecute.Dock = DockStyle.Fill;
            voiceText.KeyDown += delegate(object sender, KeyEventArgs args) {
                if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; SubmitVoiceText(); }
            };
            inputRow.Controls.Add(voiceText, 0, 0); inputRow.Controls.Add(voiceExecute, 1, 0);
            layout.Controls.Add(inputRow, 0, 5);
            voicePanel.Controls.Add(layout);
            Controls.Add(voicePanel);
            voicePanel.BringToFront();
            VisibleChanged += delegate { if (!Visible) StopVoiceInput(false); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) StopVoiceInput(false); };
        }

        private Button MakeVoiceButton(string caption, EventHandler action)
        {
            Button button = new Button { Text = caption, AutoSize = true, Height = 32,
                MinimumSize = new Size(88, 30), Font = new Font("Microsoft YaHei UI", 9f),
                AccessibleName = caption, UseVisualStyleBackColor = true };
            button.Click += action;
            return button;
        }

        internal void RequestVoiceInput()
        {
            if (voiceDisposed || shuttingDown || IsDisposed) return;
            StopVoiceInput(false);
            OpenList(false);
            voicePanel.Visible = true;
            voicePanel.BringToFront();
            AppendVoice("小deepseek", "是否开启语音输入？开启后，我会根据你的话添加待办，或为已完成的事情打勾。点击“开启语音输入”才会使用麦克风。");
            voiceStart.Focus();
        }

        private void AppendVoice(string who, string message)
        {
            if (voiceDisposed || voiceConversation == null || voiceConversation.IsDisposed) return;
            if (voiceConversation.TextLength > 12000) voiceConversation.Text = voiceConversation.Text.Substring(6000);
            voiceConversation.AppendText(who + "：" + message + Environment.NewLine);
            voiceConversation.SelectionStart = voiceConversation.TextLength;
            voiceConversation.ScrollToCaret();
        }

        private void VoiceOnUi(Action action)
        {
            if (voiceDisposed || shuttingDown || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { if (!voiceDisposed && !shuttingDown && !IsDisposed) action(); }); }
            catch (InvalidOperationException) { }
        }

        private void StartVoiceInput()
        {
            if (voiceDisposed || voiceBusy) return;
            if (!loaded) { AppendVoice("小deepseek", "待办清单正在加载，请稍后再开启语音。"); StartBackground(); return; }
            StopVoiceInput(false);
            VoiceInputSession session = new VoiceInputSession();
            voiceSession = session;
            int epoch = voiceEpoch;
            session.Recognized += delegate(string text) {
                VoiceOnUi(delegate {
                    if (voiceSession != session || epoch != voiceEpoch || !session.IsListening || !Visible) return;
                    ProcessVoiceText(text, epoch, true);
                });
            };
            session.StatusChanged += delegate(string message) {
                VoiceOnUi(delegate {
                    if (voiceSession != session || epoch != voiceEpoch) return;
                    voiceStatus.Text = session.IsListening ? "● 麦克风已开启 · 正在聆听（最多 90 秒）" : "麦克风未开启";
                    voiceStatus.ForeColor = session.IsListening ? Color.FromArgb(169, 49, 38) : Color.FromArgb(68, 81, 107);
                    voiceStart.Enabled = !session.IsListening;
                    voiceStop.Enabled = session.IsListening;
                    AppendVoice("小deepseek", message);
                });
            };
            session.Start();
        }

        private void StopVoiceInput(bool announce)
        {
            voiceEpoch++;
            VoiceInputSession previous = voiceSession;
            voiceSession = null;
            if (previous != null) previous.Dispose();
            if (voiceDisposed) return;
            if (voiceStatus != null) { voiceStatus.Text = "麦克风未开启"; voiceStatus.ForeColor = Color.FromArgb(68, 81, 107); }
            if (voiceStart != null) voiceStart.Enabled = true;
            if (voiceStop != null) voiceStop.Enabled = false;
            if (announce) AppendVoice("小deepseek", "语音输入已停止，麦克风已释放。");
        }

        private void SubmitVoiceText()
        {
            string text = voiceText.Text.Trim();
            if (text.Length == 0 || voiceBusy) return;
            voiceText.Clear();
            ProcessVoiceText(text, voiceEpoch, false);
        }

        private async void ProcessVoiceText(string text, int epoch, bool fromMicrophone)
        {
            AppendVoice("你", text);
            if (System.Text.RegularExpressions.Regex.IsMatch(text.Trim(),
                @"^(?:请|帮我)?(?:停止语音|关闭语音|停止录音|关闭麦克风)(?:输入)?[。！!，,\s]*$"))
            { StopVoiceInput(true); return; }
            if (voiceBusy) { AppendVoice("小deepseek", "上一句还在处理中，请稍后再说一次。"); return; }
            voiceBusy = true; voiceExecute.Enabled = false;
            try
            {
                if (!loaded || browser.CoreWebView2 == null)
                { AppendVoice("小deepseek", "清单还没有准备好，请稍后重试这句话。"); return; }
                string raw = await browser.CoreWebView2.ExecuteScriptAsync(
                    "window.littleDeepSeekVoice ? window.littleDeepSeekVoice.snapshot() : null");
                if (!CanApplyVoice(epoch, fromMicrophone)) return;
                VoiceSnapshot snapshot = voiceJson.Deserialize<VoiceSnapshot>(raw);
                if (snapshot == null || snapshot.tasks == null)
                { AppendVoice("小deepseek", "待办页面尚未准备好，请稍后重试。"); return; }
                if (!string.IsNullOrEmpty(snapshot.storageError))
                { AppendVoice("小deepseek", snapshot.storageError + " 请先恢复清单存储再使用语音。"); return; }
                VoiceTodoPlan plan = VoiceTodoInterpreter.Interpret(text, snapshot.tasks);
                if (plan.operations.Count == 0) { AppendVoice("小deepseek", plan.message); return; }
                string operations = voiceJson.Serialize(plan.operations);
                string expected = voiceJson.Serialize(snapshot.tasks);
                if (!CanApplyVoice(epoch, fromMicrophone)) return;
                string resultJson = await browser.CoreWebView2.ExecuteScriptAsync(
                    "window.littleDeepSeekVoice ? window.littleDeepSeekVoice.apply(" + operations + "," + expected + ") : {ok:false,error:'待办页面已重新加载，请再说一次。'}");
                if (voiceDisposed || shuttingDown || IsDisposed) return;
                VoiceApplyResult result = voiceJson.Deserialize<VoiceApplyResult>(resultJson);
                AppendVoice("小deepseek", result != null && result.ok ? plan.message :
                    (result == null ? "待办更新失败，请重试。" : result.error));
            }
            catch (Exception error)
            {
                if (!voiceDisposed && !shuttingDown && !IsDisposed)
                    AppendVoice("小deepseek", "暂时无法更新待办，请重新打开清单后重试。（" + error.Message + "）");
            }
            finally
            {
                voiceBusy = false;
                if (!voiceDisposed && !voiceExecute.IsDisposed) voiceExecute.Enabled = true;
            }
        }

        private bool CanApplyVoice(int epoch, bool fromMicrophone)
        {
            return !voiceDisposed && !shuttingDown && !IsDisposed && Visible && voicePanel.Visible
                && epoch == voiceEpoch && (!fromMicrophone || (voiceSession != null && voiceSession.IsListening));
        }

        partial void DisposeVoicePanel()
        {
            if (voiceDisposed) return;
            StopVoiceInput(false);
            voiceDisposed = true;
        }
    }
}
