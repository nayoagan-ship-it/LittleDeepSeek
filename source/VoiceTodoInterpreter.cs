using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LittleDeepSeek
{
    // Public fields deliberately match the small JSON objects exchanged with todo-list.
    internal sealed class VoiceTodoItem
    {
        public string id;
        public string text;
        public bool done;
    }

    internal sealed class VoiceTodoOperation
    {
        public string action;
        public string text;
        public string id;
    }

    internal sealed class VoiceTodoPlan
    {
        public List<VoiceTodoOperation> operations = new List<VoiceTodoOperation>();
        public string message;
        public bool needsClarification;
    }

    internal static class VoiceTodoInterpreter
    {
        private const string AddPrefix = @"^(?:添加|新增|新建|创建|加一[个条项])(?:一[个条项]|一下|个)?(?:待办事项|待办|任务)?(?:[：:\s]*)|^(?:记下|记一下|记个待办|提醒我|加入(?:待办事项|待办|任务)(?:清单)?)(?:[：:\s]*)";
        private const string CommandStart = @"(?:帮我|替我|给我|请|麻烦|添加|新增|新建|创建|记下|记一下|提醒我|把|将|我已经|已经|完成|标记)";
        private const string Temporal = @"(?:今天|明天|后天|今晚|明早|明晚|这周|本周|下周|下个星期|这个星期|下个月|本月|周[一二三四五六日天]|星期[一二三四五六日天]|[一二三四五六七八九十0-9]+[月号日点时])";

        internal static VoiceTodoPlan Interpret(string utterance, IList<VoiceTodoItem> tasks)
        {
            if (String.IsNullOrWhiteSpace(utterance))
                return Clarify("没有听清，请说“添加明天买牛奶”或“把买牛奶打勾”。");
            if (utterance.Length > 6000)
                return Clarify("这段话太长，请分成几次说明要添加或完成的待办。");

            string input = StripPoliteness(utterance.Trim());
            string[] clauses = SplitOutsideQuotes(input, true);
            if (clauses.Length > 20)
                return Clarify("一次最多处理二十条指令，请分几次说。");

            VoiceTodoPlan plan = new VoiceTodoPlan();
            List<string> added = new List<string>();
            List<string> completed = new List<string>();
            List<string> unchanged = new List<string>();
            HashSet<string> operationKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (string rawClause in clauses)
            {
                string clause = TrimClause(rawClause);
                if (clause.Length == 0) continue;
                clause = StripPoliteness(clause);
                Match add = Regex.Match(clause, AddPrefix);
                if (add.Success)
                {
                    string addText = clause.Substring(add.Length).Trim();
                    // An explicit add command may name a negative habit, e.g. 不要熬夜.
                    // A question about adding is still not an instruction to mutate.
                    if (IsQuestion(addText)) return Clarify("请直接说明要添加的待办，例如“添加明天买牛奶”。");
                    VoiceTodoPlan error = AddTasks(addText, tasks, plan, added, unchanged, operationKeys);
                    if (error != null) return error;
                    continue;
                }

                if (IsQuestion(clause))
                    return Clarify("这句话像是在询问状态。请明确说“添加……”或“把……打勾”。");
                if (Regex.IsMatch(clause, @"(?:还没|尚未|未曾|并未|没(?:有)?|不要|不必|不用|不需要|别|取消|未完成|未做完)"))
                    return Clarify("听到了否定或尚未完成的意思，待办没有改变。完成后可说“把任务名称打勾”。");

                // Preserve the date in an intended future task. Never interpret a
                // future '要完成' as an assertion that the work is already finished.
                string future = FutureTask(clause);
                if (future != null)
                {
                    VoiceTodoPlan error = AddTasks(future, tasks, plan, added, unchanged, operationKeys);
                    if (error != null) return error;
                    continue;
                }
                if (Regex.IsMatch(clause, @"(?:准备|打算|计划|想要|将要|即将|争取|希望|应该|必须|需要|要去|要完成)"))
                    return Clarify("这是准备做的事情，还不会打勾。请说“添加……”来创建待办。");
                if (Regex.IsMatch(clause, @"^(?:我)?(?:明天|后天|明早|明晚|下周|下个月|下个星期)"))
                    return Clarify("听到了未来的安排。请说“添加……”创建待办，或明确说“把任务名称打勾”。");
                if (Regex.IsMatch(clause, @"^(?:等(?:我|到|你)|如果|假如|假设|待(?:我|到)|在.*(?:之前|之后|以后|以前))"))
                    return Clarify("这句话含有等待或条件，还不能确定已经完成。请说“添加……”或“把任务名称打勾”。");

                string target = CompletionTarget(clause);
                if (target == null)
                    return Clarify("还不确定要怎样修改待办。请说“添加明天买牛奶”或“把买牛奶打勾”。");

                // A conjunction in a completion command names multiple targets.
                string[] targets = SplitTitles(target);
                foreach (string part in targets)
                {
                    string title = TrimTitle(part);
                    if (title.Length == 0 || title.Length > 500 || Normalize(title).Length < 2)
                        return Clarify("请说出要打勾的完整待办名称。");
                    List<VoiceTodoItem> matches = MatchTasks(title, tasks);
                    if (matches.Count == 0)
                        return Clarify("没有找到“" + title + "”对应的待办，请说完整名称，或先说“添加……”。");
                    if (matches.Count > 1)
                    {
                        List<string> names = new List<string>();
                        for (int i = 0; i < matches.Count && i < 5; i++) names.Add("“" + matches[i].text + "”");
                        return Clarify("“" + title + "”对应多条待办：" + String.Join("、", names.ToArray()) +
                            (matches.Count > 5 ? "等" : "") + "。请说更完整的名称；同名任务请在清单中选择。此次没有修改待办。");
                    }
                    VoiceTodoItem item = matches[0];
                    if (item.done)
                    {
                        unchanged.Add("“" + item.text + "”已经完成");
                    }
                    else if (String.IsNullOrWhiteSpace(item.id))
                    {
                        return Clarify("这条待办暂时无法定位，请在清单加载完成后重试。");
                    }
                    else if (operationKeys.Add("complete:" + item.id))
                    {
                        plan.operations.Add(new VoiceTodoOperation { action = "complete", id = item.id, text = item.text });
                        completed.Add(item.text);
                    }
                }
            }

            if (plan.operations.Count > 20)
                return Clarify("一次最多修改二十条待办，请分几次说。此次没有修改待办。");
            List<string> messages = new List<string>();
            if (added.Count > 0) messages.Add("添加待办：" + String.Join("、", added.ToArray()));
            if (completed.Count > 0) messages.Add("勾选完成：" + String.Join("、", completed.ToArray()));
            if (unchanged.Count > 0) messages.Add(String.Join("；", unchanged.ToArray()) + "，无需重复操作");
            if (messages.Count == 0) return Clarify("没有听清要修改的待办，请再说一次。");
            plan.message = String.Join("；", messages.ToArray()) + "。";
            return plan;
        }

        private static VoiceTodoPlan AddTasks(string text, IList<VoiceTodoItem> tasks, VoiceTodoPlan plan,
            List<string> added, List<string> unchanged, HashSet<string> operationKeys)
        {
            string[] titles = SplitTitles(text);
            if (titles.Length > 20) return Clarify("一次最多添加二十条待办，请分几次说。");
            foreach (string part in titles)
            {
                string title = TrimTitle(part);
                if (title.Length == 0) return Clarify("请补充待办内容，例如“添加明天买牛奶”。");
                if (title.Length > 500) return Clarify("单条待办不能超过五百字，请缩短后再说。");
                string normalized = Normalize(title);
                if (normalized.Length < 2) return Clarify("请把待办内容说完整一些。");
                bool exists = false;
                if (tasks != null)
                    foreach (VoiceTodoItem item in tasks)
                        if (item != null && !item.done && Normalize(item.text) == normalized) { exists = true; break; }
                if (exists)
                {
                    unchanged.Add("“" + title + "”已在待办中");
                    continue;
                }
                if (operationKeys.Add("add:" + normalized))
                {
                    plan.operations.Add(new VoiceTodoOperation { action = "add", text = title });
                    added.Add(title);
                }
            }
            return null;
        }

        private static string CompletionTarget(string clause)
        {
            Match match = Regex.Match(clause, @"^(?:把|将)\s*(.+?)\s*(?:打(?:上)?[勾钩]|勾选|标记(?:为|成)?(?:已)?完成|设(?:置)?(?:为|成)?(?:已)?完成)(?:了)?$");
            if (match.Success) return match.Groups[1].Value;
            match = Regex.Match(clause, @"^(?:标记|设(?:置)?)\s*(.+?)\s*(?:为|成)(?:已)?完成(?:了)?$");
            if (match.Success) return match.Groups[1].Value;
            match = Regex.Match(clause, @"^(?:我)?(?:已经|已)?(?:完成|搞定)(?:了)?\s*(.+)$");
            if (match.Success) return TrimFinalParticle(match.Groups[1].Value);
            match = Regex.Match(clause, @"^(.+?)\s*(?:已经|已)?(?:完成|做完|搞定)(?:了)?$");
            if (match.Success) return Regex.Replace(match.Groups[1].Value, @"^(?:我)?(?:已经|已)", "");

            // A bare 买完牛奶 may be an intention. Require a completed statement,
            // and reject dependent clauses such as 买完牛奶再回家 before matching.
            if ((!Regex.IsMatch(clause, @"^(?:我)?(?:已经|已|刚刚|刚)") && !clause.EndsWith("了", StringComparison.Ordinal)) ||
                Regex.IsMatch(clause, @"(?:再|以后|之后|之前|的时候|以前|就好)")) return null;
            string statement = Regex.Replace(clause, @"^(?:我)?(?:已经|已|刚刚|刚)", "");
            statement = TrimFinalParticle(statement);
            if (!Regex.IsMatch(statement, @"(?:[买写读看做吃跑学交扫洗修画背练]完)")) return null;
            // Convert a completed verb to its task form without inventing semantic
            // synonyms: 买完牛奶 -> 买牛奶; 写完报告 -> 写报告.
            return Regex.Replace(statement, @"([买写读看做吃跑学交扫洗修画背练])完", "$1");
        }

        private static string FutureTask(string clause)
        {
            Match match = Regex.Match(clause, @"^(?:我)?(" + Temporal + @".*?)?(?:要|需要|打算|准备|计划)(.+)$");
            if (!match.Success) return null;
            // Without a date, only a first-person statement gives enough context.
            if (!match.Groups[1].Success && !clause.StartsWith("我", StringComparison.Ordinal)) return null;
            return match.Groups[1].Value + match.Groups[2].Value;
        }

        private static List<VoiceTodoItem> MatchTasks(string target, IList<VoiceTodoItem> tasks)
        {
            List<VoiceTodoItem> exact = new List<VoiceTodoItem>();
            List<VoiceTodoItem> partial = new List<VoiceTodoItem>();
            if (tasks == null) return exact;
            string needle = Normalize(target);
            foreach (VoiceTodoItem item in tasks)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.text)) continue;
                string title = Normalize(item.text);
                if (title == needle) exact.Add(item);
                else if (title.IndexOf(needle, StringComparison.Ordinal) >= 0) partial.Add(item);
            }
            return exact.Count > 0 ? exact : partial;
        }

        private static string[] SplitTitles(string text)
        {
            return SplitOutsideQuotes(text.Trim(), false);
        }

        private static string[] SplitOutsideQuotes(string text, bool clauses)
        {
            List<string> pieces = new List<string>();
            StringBuilder current = new StringBuilder();
            char closeQuote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (closeQuote != '\0')
                {
                    current.Append(c);
                    if (c == closeQuote) closeQuote = '\0';
                    continue;
                }
                if (c == '“' || c == '\"' || c == '‘')
                {
                    closeQuote = c == '“' ? '”' : c == '‘' ? '’' : '\"';
                    current.Append(c);
                    continue;
                }
                int separatorLength = 0;
                if (clauses)
                {
                    if (c == '。' || c == '；' || c == ';' || c == '\r' || c == '\n') separatorLength = 1;
                    else if (c == ',' || c == '，')
                    {
                        if (Regex.IsMatch(text.Substring(i + 1), @"^\s*(?:(?:然后|接着|另外|并且|同时|再)\s*)?" + CommandStart))
                            separatorLength = 1;
                    }
                    else if (c == '然' || c == '接' || c == '另' || c == '并' || c == '同' || c == '再')
                    {
                        Match split = Regex.Match(text.Substring(i), @"^(?:然后|接着|另外|并且|同时|再)\s*(?=" + CommandStart + @")");
                        if (split.Success) separatorLength = split.Length;
                    }
                }
                else
                {
                    if (c == '和' || c == '、') separatorLength = 1;
                    else if ((c == '以' || c == '还') && i + 1 < text.Length &&
                        (text.Substring(i, 2) == "以及" || text.Substring(i, 2) == "还有")) separatorLength = 2;
                }
                if (separatorLength == 0) { current.Append(c); continue; }
                if (!clauses || current.ToString().Trim().Length > 0) pieces.Add(current.ToString());
                current.Length = 0;
                i += separatorLength - 1;
            }
            if (!clauses || current.ToString().Trim().Length > 0) pieces.Add(current.ToString());
            return pieces.ToArray();
        }

        private static bool IsQuestion(string text)
        {
            return Regex.IsMatch(text, @"[？?]|(?:吗|么|呢)[。！!\s]*$|(?:是否|是不是|有没有|能不能|可不可以|能否|怎么|如何|为什么|要不要)");
        }

        private static string StripPoliteness(string text)
        {
            text = Regex.Replace(text, @"^小deepseek[，,\s]*", "", RegexOptions.IgnoreCase);
            return Regex.Replace(text, @"^(?:(?:请(?:你)?|麻烦(?:你)?|帮我|替我|给我)[，,\s]*)+", "").Trim();
        }

        private static string TrimFinalParticle(string text)
        {
            return Regex.Replace(text.Trim(), @"了$", "");
        }

        private static string TrimTitle(string text)
        {
            return (text ?? String.Empty).Trim().Trim('。', '！', '!', '，', ',', '：', ':', '“', '”', '\"', '‘', '’', '\'').Trim();
        }

        private static string TrimClause(string text)
        {
            return (text ?? String.Empty).Trim().Trim('。', '！', '!', '，', ',', '：', ':').Trim();
        }

        private static string Normalize(string text)
        {
            return Regex.Replace(text ?? String.Empty, @"[\s\p{P}]+", String.Empty).ToLowerInvariant();
        }

        private static VoiceTodoPlan Clarify(string message)
        {
            return new VoiceTodoPlan { message = message, needsClarification = true };
        }
    }
}
