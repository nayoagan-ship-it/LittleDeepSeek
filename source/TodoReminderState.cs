using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace LittleDeepSeek
{
    // The embedded list has no due dates. Unfinished items remain today's tasks;
    // only an observed unfinished -> finished transition supplies a completion date.
    internal sealed class TodoReminderState
    {
        private const int MaxJsonLength = 4 * 1024 * 1024;
        private const int MaxTasks = 10000;
        private readonly string statePath;
        private List<TodoItem> items = new List<TodoItem>();
        private readonly Dictionary<string, string> praisedOn = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> pendingPraise = new List<string>();
        private int pendingCursor;
        private int completedCursor;

        internal bool HasSnapshot { get; private set; }

        internal TodoReminderState(string statePath)
        {
            this.statePath = statePath;
            Load();
        }

        // Invalid/unavailable input never replaces a valid snapshot. A disk error
        // is contained so reminders can still work for this running session.
        internal bool UpdateSnapshot(string rawJson, DateTime now)
        {
            List<TodoItem> incoming;
            if (!TryReadSnapshot(rawJson, out incoming)) return false;
            string today = Day(now);
            Dictionary<string, TodoItem> previous = Index(items);
            bool changed = !HasSnapshot || items.Count != incoming.Count;
            for (int i = 0; i < incoming.Count; i++)
            {
                TodoItem item = incoming[i], old;
                if (previous.TryGetValue(item.Id, out old))
                {
                    if (item.Done && old.Done) item.CompletedOn = old.CompletedOn;
                    if (item.Done && !old.Done)
                    {
                        item.CompletedOn = today;
                        string lastDay;
                        if ((!praisedOn.TryGetValue(item.Id, out lastDay) || lastDay != today) && !pendingPraise.Contains(item.Id))
                            pendingPraise.Add(item.Id);
                    }
                }
                if (i >= items.Count || !SameItem(items[i], item)) changed = true;
            }
            items = incoming;
            HasSnapshot = true;
            Dictionary<string, TodoItem> current = Index(items);
            for (int i = pendingPraise.Count - 1; i >= 0; i--)
            {
                TodoItem item;
                if (!current.TryGetValue(pendingPraise[i], out item) || !item.Done)
                {
                    pendingPraise.RemoveAt(i);
                    changed = true;
                }
            }
            // Keep enough history to suppress same-day repeat completions, including
            // deleted/recreated IDs, without making the ledger grow indefinitely.
            List<string> expired = new List<string>();
            foreach (KeyValuePair<string, string> pair in praisedOn)
            {
                DateTime day;
                if (TryDay(pair.Value, out day) && day < now.Date.AddDays(-32)) expired.Add(pair.Key);
            }
            foreach (string id in expired) { praisedOn.Remove(id); changed = true; }
            if (changed) Save();
            return true;
        }

        internal string TakeCompletionMessage(DateTime now)
        {
            if (pendingPraise.Count == 0) return null;
            Dictionary<string, TodoItem> current = Index(items);
            List<TodoItem> completed = new List<TodoItem>();
            string today = Day(now);
            foreach (string id in pendingPraise)
            {
                TodoItem item;
                string lastDay;
                if (current.TryGetValue(id, out item) && item.Done &&
                    (!praisedOn.TryGetValue(id, out lastDay) || lastDay != today))
                {
                    completed.Add(item);
                    praisedOn[id] = today;
                }
            }
            pendingPraise.Clear();
            Save();
            if (completed.Count == 0) return null;
            List<string> lines = new List<string>();
            lines.Add("完成啦，给你一朵小花！");
            lines.Add("· " + ShortTitle(completed[0].Text, 11));
            if (completed.Count > 1) lines.Add("· " + ShortTitle(completed[1].Text, 11));
            lines.Add(completed.Count > 2 ? "完成 " + completed.Count + " 件，真棒！" : "你又前进了一步，真棒！");
            return string.Join("\n", lines.ToArray());
        }

        internal string BuildSummary(DateTime now, bool explicitRequest)
        {
            if (!HasSnapshot) return explicitRequest ? "还没读到待办清单呢。\n打开 todo-list 后，\n我就能陪你安排今天啦～" : null;
            if (items.Count == 0) return explicitRequest ? "todo-list 还是空的呢～\n写下一件今天想做的事，\n我会陪你一步步完成！" : null;
            List<TodoItem> unfinished = new List<TodoItem>();
            List<TodoItem> finished = new List<TodoItem>();
            List<TodoItem> finishedToday = new List<TodoItem>();
            string today = Day(now);
            foreach (TodoItem item in items)
            {
                if (!item.Done) unfinished.Add(item);
                else
                {
                    finished.Add(item);
                    if (item.CompletedOn == today) finishedToday.Add(item);
                }
            }
            List<TodoItem> featured = finishedToday.Count > 0 ? finishedToday : finished;
            List<string> lines = new List<string>();
            if (unfinished.Count > 0)
            {
                lines.Add("今日待办 " + unfinished.Count + " 件：");
                lines.Add("· " + NextTitle(unfinished, ref pendingCursor, 11));
                if (finished.Count > 0)
                {
                    lines.Add("已完成：" + NextTitle(featured, ref completedCursor, 11));
                    lines.Add(finishedToday.Count > 0 ? "今天完成 " + finishedToday.Count + " 件，真棒！" : "已完成 " + finished.Count + " 件，真棒！");
                }
                else
                {
                    if (unfinished.Count > 1) lines.Add("· " + NextTitle(unfinished, ref pendingCursor, 11));
                    lines.Add("从一小步开始，我陪你～");
                }
            }
            else
            {
                lines.Add("清单都完成啦，真棒！");
                lines.Add("· " + NextTitle(featured, ref completedCursor, 11));
                lines.Add(finishedToday.Count > 0 ? "今天已完成 " + finishedToday.Count + " 件～" : "已完成 " + finished.Count + " 件，辛苦啦～");
            }
            Save();
            return string.Join("\n", lines.ToArray());
        }

        internal void SkipPendingPraise()
        {
            if (pendingPraise.Count == 0) return;
            Dictionary<string, TodoItem> current = Index(items);
            foreach (string id in pendingPraise)
            {
                TodoItem item;
                if (current.TryGetValue(id, out item) && item.CompletedOn != null) praisedOn[id] = item.CompletedOn;
            }
            pendingPraise.Clear();
            Save();
        }

        private static string NextTitle(List<TodoItem> choices, ref int cursor, int limit)
        {
            if (cursor < 0 || cursor >= choices.Count) cursor = 0;
            string title = ShortTitle(choices[cursor].Text, limit);
            cursor = (cursor + 1) % choices.Count;
            return title;
        }

        private static string ShortTitle(string text, int limit)
        {
            StringBuilder singleLine = new StringBuilder();
            bool gap = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { gap = singleLine.Length > 0; continue; }
                // Bidi formatting from a task title must not reorder the pet's labels.
                if ((c >= '\u202a' && c <= '\u202e') || (c >= '\u2066' && c <= '\u2069')) continue;
                if (gap) { singleLine.Append(' '); gap = false; }
                singleLine.Append(c);
            }
            StringInfo info = new StringInfo(singleLine.ToString());
            return info.LengthInTextElements <= limit ? info.String : info.SubstringByTextElements(0, limit - 1) + "…";
        }

        private static bool SameItem(TodoItem left, TodoItem right)
        {
            return left.Id == right.Id && left.Text == right.Text && left.Done == right.Done && left.CompletedOn == right.CompletedOn;
        }

        private static Dictionary<string, TodoItem> Index(List<TodoItem> source)
        {
            Dictionary<string, TodoItem> result = new Dictionary<string, TodoItem>(StringComparer.Ordinal);
            foreach (TodoItem item in source) result.Add(item.Id, item);
            return result;
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxJsonLength, RecursionLimit = 16 };
        }

        private static bool TryReadSnapshot(string rawJson, out List<TodoItem> result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(rawJson) || rawJson.Length > MaxJsonLength) return false;
            try { return TryReadItems(Serializer().DeserializeObject(rawJson), false, out result); }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static bool TryReadItems(object value, bool stored, out List<TodoItem> result)
        {
            result = null;
            IList array = value as IList;
            if (array == null || array.Count > MaxTasks) return false;
            List<TodoItem> parsed = new List<TodoItem>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (object entry in array)
            {
                Dictionary<string, object> obj = entry as Dictionary<string, object>;
                object idValue, textValue, doneValue;
                if (obj == null || !obj.TryGetValue("id", out idValue) || !obj.TryGetValue("text", out textValue) || !obj.TryGetValue("done", out doneValue)) return false;
                string id = idValue as string, text = textValue as string;
                if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || string.IsNullOrWhiteSpace(text) || text.Length > 10000 || !(doneValue is bool) || !ids.Add(id)) return false;
                TodoItem item = new TodoItem { Id = id, Text = text, Done = (bool)doneValue };
                object completedValue;
                if (stored && obj.TryGetValue("completedOn", out completedValue) && completedValue != null)
                {
                    DateTime date;
                    item.CompletedOn = completedValue as string;
                    if (!item.Done || !TryDay(item.CompletedOn, out date)) return false;
                }
                parsed.Add(item);
            }
            result = parsed;
            return true;
        }

        private static string Day(DateTime now) { return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        private static bool TryDay(string value, out DateTime date)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(statePath) || new FileInfo(statePath).Length > MaxJsonLength * 4L) return;
                string json = File.ReadAllText(statePath, Encoding.UTF8);
                if (json.Length > MaxJsonLength) return;
                Dictionary<string, object> root = Serializer().DeserializeObject(json) as Dictionary<string, object>;
                object version, snapshot, taskData, praiseData, queueData, pendingIndex, completedIndex;
                if (root == null || !root.TryGetValue("version", out version) || !(version is int) || (int)version != 1 ||
                    !root.TryGetValue("hasSnapshot", out snapshot) || !(snapshot is bool) || !(bool)snapshot ||
                    !root.TryGetValue("items", out taskData) || !root.TryGetValue("praisedOn", out praiseData) ||
                    !root.TryGetValue("pendingPraise", out queueData) || !root.TryGetValue("pendingCursor", out pendingIndex) ||
                    !root.TryGetValue("completedCursor", out completedIndex) || !(pendingIndex is int) || !(completedIndex is int) ||
                    (int)pendingIndex < 0 || (int)completedIndex < 0) return;
                List<TodoItem> loaded;
                if (!TryReadItems(taskData, true, out loaded)) return;
                Dictionary<string, object> ledger = praiseData as Dictionary<string, object>;
                IList queue = queueData as IList;
                if (ledger == null || ledger.Count > MaxTasks * 2 || queue == null || queue.Count > MaxTasks) return;
                Dictionary<string, string> validLedger = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> pair in ledger)
                {
                    DateTime date;
                    string value = pair.Value as string;
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 256 || !TryDay(value, out date)) return;
                    validLedger.Add(pair.Key, value);
                }
                Dictionary<string, TodoItem> byId = Index(loaded);
                List<string> validQueue = new List<string>();
                HashSet<string> queueIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (object value in queue)
                {
                    string id = value as string;
                    TodoItem item;
                    if (id == null || !queueIds.Add(id) || !byId.TryGetValue(id, out item) || !item.Done || item.CompletedOn == null) return;
                    validQueue.Add(id);
                }
                items = loaded;
                foreach (KeyValuePair<string, string> pair in validLedger) praisedOn.Add(pair.Key, pair.Value);
                pendingPraise.AddRange(validQueue);
                pendingCursor = (int)pendingIndex;
                completedCursor = (int)completedIndex;
                HasSnapshot = true;
            }
            catch (Exception) { /* Missing, locked, or corrupt cache starts without user data. */ }
        }

        private void Save()
        {
            string temporaryPath = null;
            try
            {
                if (!HasSnapshot || string.IsNullOrWhiteSpace(statePath)) return;
                List<object> taskData = new List<object>();
                foreach (TodoItem item in items)
                    taskData.Add(new Dictionary<string, object> { { "id", item.Id }, { "text", item.Text }, { "done", item.Done }, { "completedOn", item.CompletedOn } });
                Dictionary<string, object> root = new Dictionary<string, object>
                {
                    { "version", 1 }, { "hasSnapshot", true }, { "items", taskData },
                    { "praisedOn", praisedOn }, { "pendingPraise", pendingPraise },
                    { "pendingCursor", pendingCursor }, { "completedCursor", completedCursor }
                };
                string destination = Path.GetFullPath(statePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryPath, Serializer().Serialize(root), new UTF8Encoding(false));
                if (File.Exists(destination)) File.Replace(temporaryPath, destination, null);
                else File.Move(temporaryPath, destination);
            }
            catch (Exception) { /* Preserve the last complete file if a save cannot finish. */ }
            finally
            {
                if (temporaryPath != null) { try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { } }
            }
        }

        private sealed class TodoItem
        {
            internal string Id;
            internal string Text;
            internal bool Done;
            internal string CompletedOn;
        }
    }
}
