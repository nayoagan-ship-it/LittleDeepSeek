using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace LittleDeepSeek
{
    internal static class TodoReminderStateTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 11, 12, 0, 0);
        private static int checks;
        private static string directory;

        private static int Main()
        {
            directory = Path.Combine(Path.GetTempPath(), "LittleDeepSeek-TodoReminderTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                InitialBaselineAndEmpty();
                CompleteDuplicateRestartAndDayBoundary();
                CancelUndoDeleteAndSkip();
                SnapshotValidationAndCorruption();
                RotationAndTitleFormatting();
                PendingSurvivesRestart();
                DiskFailureIsContained();
                Console.WriteLine("PASS: " + checks + " todo reminder assertions.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                try
                {
                    string fullPath = Path.GetFullPath(directory);
                    string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                    if (string.Equals(Path.GetDirectoryName(fullPath), tempRoot, StringComparison.OrdinalIgnoreCase) &&
                        Path.GetFileName(fullPath).StartsWith("LittleDeepSeek-TodoReminderTests-", StringComparison.Ordinal))
                        Directory.Delete(fullPath, true);
                }
                catch { }
            }
        }

        private static TodoReminderState State(string name) { return new TodoReminderState(Path.Combine(directory, name + ".json")); }
        private static object Item(string id, string title, bool done) { return new { id = id, text = title, done = done }; }
        private static string Snapshot(params object[] items) { return new JavaScriptSerializer().Serialize(items); }
        private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Assertion failed: " + message); }

        private static void InitialBaselineAndEmpty()
        {
            TodoReminderState state = State("baseline");
            Check(!state.HasSnapshot, "missing state is unknown, not an empty list");
            Check(state.BuildSummary(Today, false) == null, "unknown state does not remind");
            Check(state.UpdateSnapshot("[]", Today), "empty snapshot accepted");
            Check(state.HasSnapshot, "empty snapshot is known");
            Check(state.BuildSummary(Today, false) == null, "empty list stays quiet");
            Check(state.BuildSummary(Today, true).Contains("空"), "explicit empty state explained");
            Check(state.UpdateSnapshot(Snapshot(Item("old", "昨天的阅读", true)), Today), "historical completion accepted");
            Check(state.TakeCompletionMessage(Today) == null, "initial completed items are not newly praised");
            string summary = state.BuildSummary(Today, true);
            Check(summary.Contains("昨天的阅读") && !summary.Contains("今天已完成"), "undated completion not called today's completion");
        }

        private static void CompleteDuplicateRestartAndDayBoundary()
        {
            string undone = Snapshot(Item("a", "整理书桌", false));
            string done = Snapshot(Item("a", "整理书桌", true));
            TodoReminderState state = State("transition");
            state.UpdateSnapshot(undone, Today);
            state.UpdateSnapshot(done, Today);
            Check(state.TakeCompletionMessage(Today).Contains("整理书桌"), "observed completion praises title");
            state.UpdateSnapshot(done, Today);
            Check(state.TakeCompletionMessage(Today) == null, "duplicate snapshot does not praise twice");
            state = State("transition");
            Check(state.HasSnapshot, "snapshot persisted across restart");
            state.UpdateSnapshot(done, Today);
            Check(state.TakeCompletionMessage(Today) == null, "restart does not repeat praise");
            Check(state.BuildSummary(Today, true).Contains("今天已完成 1 件"), "completion date survived restart");
            state.UpdateSnapshot(undone, Today);
            state.UpdateSnapshot(done, Today);
            Check(state.TakeCompletionMessage(Today) == null, "undo-redo same day not repeated");
            Check(!state.BuildSummary(Today.AddDays(1), true).Contains("今天已完成"), "yesterday's completion not called today");
            state.UpdateSnapshot(undone, Today.AddDays(1));
            state.UpdateSnapshot(done, Today.AddDays(1));
            Check(state.TakeCompletionMessage(Today.AddDays(1)) != null, "next day's real completion can be praised");
        }

        private static void CancelUndoDeleteAndSkip()
        {
            string undone = Snapshot(Item("a", "写日记", false));
            string done = Snapshot(Item("a", "写日记", true));
            TodoReminderState state = State("cancel");
            state.UpdateSnapshot(undone, Today); state.UpdateSnapshot(done, Today); state.UpdateSnapshot(undone, Today);
            Check(state.TakeCompletionMessage(Today) == null, "undo before display cancels praise");
            state.UpdateSnapshot(done, Today); state.UpdateSnapshot("[]", Today);
            Check(state.TakeCompletionMessage(Today) == null, "delete before display cancels praise");
            state.UpdateSnapshot(undone, Today); state.UpdateSnapshot(done, Today); state.SkipPendingPraise();
            Check(state.TakeCompletionMessage(Today) == null, "disabled reminders clear pending praise");
            state.UpdateSnapshot(undone, Today); state.UpdateSnapshot(done, Today);
            Check(state.TakeCompletionMessage(Today) == null, "reenabling does not replay skipped same-day work");
            state.UpdateSnapshot("[]", Today); state.UpdateSnapshot(undone, Today); state.UpdateSnapshot(done, Today);
            Check(state.TakeCompletionMessage(Today) == null, "same ID recreation keeps same-day deduplication");
        }

        private static void SnapshotValidationAndCorruption()
        {
            TodoReminderState state = State("validation");
            state.UpdateSnapshot(Snapshot(Item("keep", "有效待办", false)), Today);
            string[] invalid = { "", "null", "{}", "[", "[null]", "[{\"id\":\"x\",\"text\":\"task\",\"done\":\"true\"}]", Snapshot(Item("dup", "a", false), Item("dup", "b", true)), Snapshot(Item("", "无编号", false)), Snapshot(Item("x", " \n\t", false)), Snapshot(Item("x", new string('a', 10001), false)) };
            foreach (string value in invalid) Check(!state.UpdateSnapshot(value, Today), "invalid snapshot rejected");
            Check(state.BuildSummary(Today, true).Contains("有效待办"), "invalid snapshot preserves last valid items");
            string corruptPath = Path.Combine(directory, "corrupt.json");
            File.WriteAllText(corruptPath, "{broken");
            TodoReminderState corrupt = new TodoReminderState(corruptPath);
            Check(!corrupt.HasSnapshot && corrupt.BuildSummary(Today, false) == null, "corrupt state fabricates no tasks");
            corrupt.UpdateSnapshot(Snapshot(Item("existing", "已有完成", true)), Today);
            Check(corrupt.TakeCompletionMessage(Today) == null, "recovery from corruption establishes baseline");
            Check(new TodoReminderState(corruptPath).HasSnapshot, "valid snapshot replaces corrupt state atomically");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "atomic saves leave no temporary files");
        }

        private static void RotationAndTitleFormatting()
        {
            TodoReminderState state = State("rotate");
            state.UpdateSnapshot(Snapshot(Item("a", "甲任务", false), Item("b", "乙任务", false), Item("c", "丙任务", false), Item("old", "原有成果", true)), Today);
            string first = state.BuildSummary(Today, false), second = state.BuildSummary(Today, false), third = state.BuildSummary(Today, false);
            Check(first.Contains("甲任务") && second.Contains("乙任务") && third.Contains("丙任务"), "rotating reminders reach every unfinished title");
            Check(first.Contains("原有成果") && first.Contains("真棒"), "mixed reminder shares completed item and praise");
            state.UpdateSnapshot(Snapshot(Item("long", "😀\n这是一个特别特别特别特别特别长的任务标题\r\n下一行", false)), Today);
            string message = state.BuildSummary(Today, false);
            Check(message.Split('\n').Length <= 4, "long title does not inject bubble lines");
            string titleLine = message.Split('\n')[1].Substring(2);
            Check(new StringInfo(titleLine).LengthInTextElements <= 11 && titleLine.EndsWith("…"), "long Unicode title safely truncated");
            Check(titleLine.StartsWith("😀"), "surrogate pair kept intact");
            TodoReminderState completed = State("completedrotation");
            completed.UpdateSnapshot(Snapshot(Item("a", "甲成果", true), Item("b", "乙成果", true)), Today);
            Check(completed.BuildSummary(Today, false).Contains("甲成果") && completed.BuildSummary(Today, false).Contains("乙成果"), "completed titles rotate too");
        }

        private static void PendingSurvivesRestart()
        {
            TodoReminderState state = State("pendingrestart");
            state.UpdateSnapshot(Snapshot(Item("a", "未显示的成果", false)), Today);
            state.UpdateSnapshot(Snapshot(Item("a", "未显示的成果", true)), Today);
            state = State("pendingrestart");
            Check(state.TakeCompletionMessage(Today).Contains("未显示的成果"), "undelivered completion survives restart");
            Check(State("pendingrestart").TakeCompletionMessage(Today) == null, "delivered transition persisted immediately");
        }

        private static void DiskFailureIsContained()
        {
            string blockedDirectory = Path.Combine(directory, "file-instead-of-folder");
            File.WriteAllText(blockedDirectory, "x");
            TodoReminderState state = new TodoReminderState(Path.Combine(blockedDirectory, "state.json"));
            Check(state.UpdateSnapshot(Snapshot(Item("a", "继续工作", false)), Today), "disk error does not reject valid live snapshot");
            state.UpdateSnapshot(Snapshot(Item("a", "继续工作", true)), Today);
            Check(state.TakeCompletionMessage(Today).Contains("继续工作"), "disk error does not break live reminders");
        }
    }
}
