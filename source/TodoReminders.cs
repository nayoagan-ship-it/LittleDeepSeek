using System;
using System.IO;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    internal sealed partial class PetForm
    {
        private TodoReminderState todoState;
        private System.Windows.Forms.Timer todoTimer;
        private ToolStripMenuItem todoReminderItem;
        private bool todoSpeech, todoSnapshotCurrent, todoSummaryRequested, todoSummaryFailed;
        private double nextTodoReminder, nextTodoPoll, nextTodoPraise, todoRequestDeadline, lastTodoSnapshot;

        private TodoForm EnsureTodoWindow()
        {
            if (todoWindow != null && !todoWindow.IsDisposed && todoWindow.NeedsRecreation)
            {
                bool wasVisible = todoWindow.Visible;
                todoWindow.Dispose();
                todoWindow = null;
                todoSnapshotCurrent = false;
                TodoForm replacement = EnsureTodoWindow();
                if (wasVisible) replacement.OpenList(false);
                return replacement;
            }
            if (todoWindow == null || todoWindow.IsDisposed)
            {
                todoWindow = new TodoForm(Path.Combine(Path.GetDirectoryName(settings.PathName), "todo-webview"), petIcon);
                todoWindow.TasksSnapshotReceived += OnTodoSnapshot;
                todoWindow.SnapshotFailed += delegate
                {
                    todoSnapshotCurrent = false;
                    if (todoSummaryRequested) todoSummaryFailed = true;
                };
                todoWindow.FormClosed += delegate { todoWindow = null; todoSnapshotCurrent = false; };
            }
            return todoWindow;
        }

        private TodoForm EnsureUserTodoWindow()
        {
            TodoForm current = EnsureTodoWindow();
            if (!current.HasBeenShown)
            {
                current.Dispose();
                if (ReferenceEquals(todoWindow, current)) todoWindow = null;
                current = EnsureTodoWindow();
            }
            return current;
        }

        private void StartTodoReminders()
        {
            if (todoTimer != null) return;
            todoState = new TodoReminderState(Path.Combine(Path.GetDirectoryName(settings.PathName), "todo-reminders.json"));
            nextTodoReminder = watch.Elapsed.TotalSeconds + 45;
            nextTodoPoll = watch.Elapsed.TotalSeconds + 30;
            nextTodoPraise = watch.Elapsed.TotalSeconds + 2;
            todoTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            todoTimer.Tick += delegate { TickTodoReminders(); };
            todoTimer.Start();
            EnsureTodoWindow().StartBackground();
        }

        private void StopTodoReminders()
        {
            if (todoTimer != null) { todoTimer.Stop(); todoTimer.Dispose(); todoTimer = null; }
        }

        private void OnTodoSnapshot(string json)
        {
            if (closing || todoState == null) return;
            todoSnapshotCurrent = todoState.UpdateSnapshot(json, DateTime.Now);
            if (todoSnapshotCurrent) { lastTodoSnapshot = watch.Elapsed.TotalSeconds; todoSummaryFailed = false; }
            if (!settings.TodoReminders) todoState.SkipPendingPraise();
            if (!todoSnapshotCurrent && todoSummaryRequested) todoSummaryFailed = true;
        }

        private void AddTodoReminderMenu(ToolStripMenuItem todos)
        {
            todos.DropDownItems.Add(new ToolStripSeparator());
            todos.DropDownItems.Add("现在提醒我", null, delegate
            {
                if (!Visible) BringPetBack();
                Wake();
                todoSummaryRequested = true;
                todoSummaryFailed = false;
                todoSnapshotCurrent = false;
                todoRequestDeadline = watch.Elapsed.TotalSeconds + 12;
                EnsureTodoWindow().RequestSnapshot();
            });
            todoReminderItem = new ToolStripMenuItem("待办提醒与完成夸夸", null, delegate
            {
                settings.TodoReminders = !settings.TodoReminders;
                if (todoState != null) todoState.SkipPendingPraise();
                if (!settings.TodoReminders && todoSpeech)
                { speechUntil = 0; todoSpeech = false; Text = "小deepseek"; RenderFrame(); }
                ScheduleTodoReminder();
                todoReminderItem.Checked = settings.TodoReminders;
                Save();
            });
            todos.DropDownItems.Add(todoReminderItem);
            ToolStripMenuItem frequency = new ToolStripMenuItem("提醒频率");
            foreach (int minutes in new int[] { 5, 10, 20 })
            {
                int captured = minutes;
                ToolStripMenuItem choice = new ToolStripMenuItem("约每 " + minutes + " 分钟", null, delegate
                {
                    settings.TodoReminderMinutes = captured;
                    ScheduleTodoReminder(); Save();
                });
                choice.Tag = minutes;
                frequency.DropDownItems.Add(choice);
            }
            frequency.DropDownOpening += delegate
            {
                foreach (ToolStripMenuItem choice in frequency.DropDownItems)
                    choice.Checked = (int)choice.Tag == settings.TodoReminderMinutes;
            };
            todos.DropDownItems.Add(frequency);
            todos.DropDownOpening += delegate { todoReminderItem.Checked = settings.TodoReminders; };
        }

        private void ScheduleTodoReminder()
        {
            nextTodoReminder = watch.Elapsed.TotalSeconds + settings.TodoReminderMinutes * 60 * (0.8 + random.NextDouble() * 0.4);
        }

        private bool CanShowTodoSpeech(double now)
        {
            return Visible && !settings.Sleep && !menu.Visible && !mouseHeld && !dragging && !clickTimer.Enabled
                && !DesktopEatingActive && !RicePoseActive && poseName == null && now >= jumpUntil && now >= happyUntil
                && now >= speechUntil;
        }

        private void ShowTodoReadFailure()
        {
            todoSummaryRequested = false;
            todoSummaryFailed = false;
            ShowTodoSpeech("暂时没读到待办清单。\n请打开 todo-list 重试～", false);
        }

        private void ShowTodoSpeech(string message, bool celebrate)
        {
            Say(message, 12);
            todoSpeech = true;
            walkUntil = 0; walkRemainder = 0;
            nextWalk = Math.Max(nextWalk, speechUntil + 2);
            if (celebrate) AddHearts(6);
            if (celebrate) StartDshAnimation("celebrate", 9.7);
            RenderFrame();
        }

        private void TickTodoReminders()
        {
            if (closing || todoState == null) return;
            double now = watch.Elapsed.TotalSeconds;
            if (todoSnapshotCurrent && now - lastTodoSnapshot > 90) todoSnapshotCurrent = false;
            if (now >= nextTodoPoll)
            {
                nextTodoPoll = now + 30;
                EnsureTodoWindow().RequestSnapshot();
            }
            if (todoSummaryRequested && !todoSnapshotCurrent && now >= todoRequestDeadline) todoSummaryFailed = true;
            if (!CanShowTodoSpeech(now)) return;
            if (todoSummaryRequested && todoSummaryFailed) { ShowTodoReadFailure(); return; }
            if (!todoSnapshotCurrent) return;
            if (todoSummaryRequested)
            {
                todoSummaryRequested = false;
                ShowTodoSpeech(todoState.BuildSummary(DateTime.Now, true), false);
                ScheduleTodoReminder();
                return;
            }
            if (!settings.TodoReminders) return;
            if (now >= nextTodoPraise)
            {
                string praise = todoState.TakeCompletionMessage(DateTime.Now);
                if (praise != null)
                {
                    ShowTodoSpeech(praise, true);
                    nextTodoPraise = now + 30;
                    ScheduleTodoReminder();
                    return;
                }
            }
            if (now < nextTodoReminder) return;
            string summary = todoState.BuildSummary(DateTime.Now, false);
            ScheduleTodoReminder();
            if (summary != null) ShowTodoSpeech(summary, false);
        }
    }
}
