using System;
using System.Globalization;
using System.Speech.Recognition;
using System.Threading;

namespace LittleDeepSeek
{
    // The owner asks for consent before calling Start and marshals callbacks to its UI.
    // Construction only inspects installed recognizers; it never opens an audio device.
    internal sealed class VoiceInputSession : IDisposable
    {
        internal const int MaximumSessionSeconds = 90;
        internal const string VoiceTypingHelp = "也可以点击下方文字框，按 Win + H 使用 Windows 语音键入，再点击“执行”。";

        private readonly object sync = new object();
        private readonly string recognizerId;
        private readonly string availabilityMessage;
        private SpeechRecognitionEngine engine;
        private Timer sessionTimer;
        private bool disposed;

        internal event Action<string> Recognized;
        internal event Action<string> StatusChanged;

        internal VoiceInputSession()
        {
            try
            {
                foreach (RecognizerInfo candidate in SpeechRecognitionEngine.InstalledRecognizers())
                {
                    if (!string.Equals(candidate.Culture.Name, "zh-CN", StringComparison.OrdinalIgnoreCase)) continue;
                    recognizerId = candidate.Id;
                    break;
                }
                availabilityMessage = recognizerId == null
                    ? "当前系统未安装简体中文语音识别，请在 Windows 语言设置中安装中文语音组件。" + VoiceTypingHelp
                    : "已找到系统简体中文语音识别；点击“开启语音输入”后才会使用麦克风。";
            }
            catch (Exception)
            {
                availabilityMessage = "暂时无法访问系统语音识别。" + VoiceTypingHelp;
            }
        }

        internal bool IsAvailable { get { return recognizerId != null; } }
        internal string AvailabilityMessage { get { return availabilityMessage; } }
        internal bool IsListening { get { lock (sync) { return !disposed && engine != null; } } }

        internal void Start()
        {
            SpeechRecognitionEngine failedEngine = null;
            string status;
            lock (sync)
            {
                if (disposed || engine != null) return;
                if (!IsAvailable)
                {
                    status = availabilityMessage;
                }
                else
                {
                    SpeechRecognitionEngine next = null;
                    try
                    {
                        next = new SpeechRecognitionEngine(recognizerId);
                        ConfigureGrammars(next);
                        next.InitialSilenceTimeout = TimeSpan.FromSeconds(15);
                        next.BabbleTimeout = TimeSpan.FromSeconds(12);
                        next.EndSilenceTimeout = TimeSpan.FromMilliseconds(800);
                        next.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(1200);
                        next.SpeechRecognized += OnRecognized;
                        next.SpeechRecognitionRejected += OnRejected;
                        next.RecognizeCompleted += OnCompleted;
                        // This is intentionally the first and only microphone access point.
                        next.SetInputToDefaultAudioDevice();
                        engine = next;
                        next.RecognizeAsync(RecognizeMode.Multiple);
                        sessionTimer = new Timer(OnSessionTimeout, next,
                            MaximumSessionSeconds * 1000, Timeout.Infinite);
                        status = "正在聆听，可以说“添加明天下午三点买牛奶”或“买牛奶已经完成”。90 秒后会自动停止。";
                    }
                    catch (Exception error)
                    {
                        engine = null;
                        failedEngine = next;
                        if (sessionTimer != null) { sessionTimer.Dispose(); sessionTimer = null; }
                        status = "语音输入未能启动，请检查默认麦克风、中文语音组件，以及 Windows 麦克风访问权限。" +
                            "（" + error.Message + "）" + VoiceTypingHelp;
                    }
                }
            }
            ReleaseEngine(failedEngine);
            RaiseStatus(status);
        }

        internal void Stop()
        {
            EndSession(null, "语音输入已停止。", false);
        }

        internal static void ConfigureGrammars(SpeechRecognitionEngine recognizer)
        {
            // Keep unrestricted speech so negations and questions reach the interpreter.
            // The additional rules improve short command words without replacing any
            // recognized words after the fact or restricting the task's free text.
            recognizer.LoadGrammar(new DictationGrammar { Name = "小deepseek中文待办听写" });
            GrammarBuilder prefix = new GrammarBuilder { Culture = new CultureInfo("zh-CN") };
            prefix.Append(new Choices("添加", "新增", "新建", "记下", "提醒我", "完成", "已经完成"));
            prefix.AppendDictation();
            recognizer.LoadGrammar(new Grammar(prefix) { Name = "小deepseek待办指令" });
            GrammarBuilder suffix = new GrammarBuilder { Culture = new CultureInfo("zh-CN") };
            suffix.AppendDictation();
            suffix.Append(new Choices("已经完成", "已经做完", "已经搞定", "完成了", "做完了", "搞定了"));
            recognizer.LoadGrammar(new Grammar(suffix) { Name = "小deepseek待办完成陈述" });
        }

        private void OnSessionTimeout(object state)
        {
            EndSession(state as SpeechRecognitionEngine, "本次语音输入已满 90 秒，麦克风已停止；需要继续时请重新开启。", false);
        }

        private void OnRecognized(object sender, SpeechRecognizedEventArgs args)
        {
            Action<string> handler;
            string text;
            lock (sync)
            {
                if (disposed || !ReferenceEquals(sender, engine) || args.Result == null) return;
                text = (args.Result.Text ?? string.Empty).Trim();
                handler = Recognized;
            }
            if (text.Length == 0) return;
            if (args.Result.Confidence < 0.35f)
            {
                RaiseStatus("这句话没有听清，请靠近麦克风再说一次，或在文字框中输入。");
                return;
            }
            if (handler != null) handler(text);
        }

        private void OnRejected(object sender, SpeechRecognitionRejectedEventArgs args)
        {
            lock (sync) { if (disposed || !ReferenceEquals(sender, engine)) return; }
            RaiseStatus("没有听清，请再说一次，或在文字框中输入。");
        }

        private void OnCompleted(object sender, RecognizeCompletedEventArgs args)
        {
            string status;
            if (args.Error != null)
                status = "语音识别中断，请检查麦克风后重新开启。" + VoiceTypingHelp;
            else if (args.InitialSilenceTimeout)
                status = "暂时没有听到说话，麦克风已停止。准备好后可以重新开启。";
            else if (args.BabbleTimeout)
                status = "周围声音较大，麦克风已停止；请靠近麦克风后重新开启。";
            else
                status = "语音输入已停止。";
            EndSession(sender as SpeechRecognitionEngine, status, false);
        }

        private void EndSession(SpeechRecognitionEngine expected, string status, bool disposing)
        {
            SpeechRecognitionEngine previous;
            lock (sync)
            {
                if (expected != null && !ReferenceEquals(expected, engine)) return;
                if (disposing) disposed = true;
                previous = engine;
                engine = null;
                if (sessionTimer != null) { sessionTimer.Dispose(); sessionTimer = null; }
            }
            ReleaseEngine(previous);
            if (previous != null && !disposing) RaiseStatus(status);
        }

        private void ReleaseEngine(SpeechRecognitionEngine previous)
        {
            if (previous == null) return;
            // Detach before cancelling: cancellation can enqueue a final recognition event.
            previous.SpeechRecognized -= OnRecognized;
            previous.SpeechRecognitionRejected -= OnRejected;
            previous.RecognizeCompleted -= OnCompleted;
            try { previous.RecognizeAsyncCancel(); } catch (Exception) { }
            try { previous.SetInputToNull(); } catch (Exception) { }
            // Dispose releases the device even when input could not be changed mid-cancellation.
            try { previous.Dispose(); } catch (Exception) { }
        }

        private void RaiseStatus(string text)
        {
            Action<string> handler;
            lock (sync) { if (disposed) return; handler = StatusChanged; }
            if (handler != null) handler(text);
        }

        public void Dispose()
        {
            EndSession(null, null, true);
            lock (sync) { Recognized = null; StatusChanged = null; }
        }
    }
}
