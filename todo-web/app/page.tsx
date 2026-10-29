'use client';

import { useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import {
  ArrowDownLeft,
  ArrowUpRight,
  Check,
  CheckCheck,
  Circle,
  CircleCheck,
  ListTodo,
  Pencil,
  Plus,
  RotateCcw,
  Trash2,
  X,
} from 'lucide-react';
import { Checkbox } from '@/components/ui/checkbox';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from '@/components/ui/empty';

import {
  createTask,
  parseTasks,
  setTaskDone,
  STORAGE_KEY,
  type Task,
  type Filter,
} from '@/lib/todos';
import { registerTodoTools } from '@/lib/todo-tools';
import { registerDesktopVoice } from '@/lib/desktop-voice';
const filters: { id: Filter; label: string }[] = [
  { id: 'all', label: '全部任务' },
  { id: 'active', label: '未完成' },
  { id: 'done', label: '已完成' },
];

export default function Home() {
  const [tasks, setTasks] = useState<Task[]>([]);
  const [ready, setReady] = useState(false);
  const [storageError, setStorageError] = useState('');
  const [draft, setDraft] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [editing, setEditing] = useState<string | null>(null);
  const [editText, setEditText] = useState('');
  const [removed, setRemoved] = useState<{ task: Task; index: number } | null>(
    null,
  );
  const [notice, setNotice] = useState('');
  const [date, setDate] = useState('');
  const input = useRef<HTMLInputElement>(null);
  const tasksRef = useRef(tasks);
  tasksRef.current = tasks;
  const storageErrorRef = useRef(storageError);
  storageErrorRef.current = storageError;
  const done = tasks.filter((task) => task.done).length;
  const active = tasks.length - done;
  const progress = tasks.length ? Math.round((done / tasks.length) * 100) : 0;

  useEffect(() => {
    setDate(
      new Intl.DateTimeFormat('zh-CN', {
        month: 'long',
        day: 'numeric',
        weekday: 'long',
      }).format(new Date()),
    );
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw !== null) {
        setTasks(parseTasks(raw));
      }
    } catch {
      setStorageError('暂时无法读取已保存的任务。本次操作只在当前页面保留。');
    }
    setReady(true);
  }, []);

  useEffect(() => {
    if (!ready || storageError) return;
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(tasks));
    } catch {
      setStorageError('暂时无法保存任务，刷新页面后可能丢失。');
    }
  }, [tasks, ready, storageError]);

  useEffect(() => {
    if (!ready) return;
    return registerTodoTools(
      () => tasksRef.current,
      (next) => {
        flushSync(() => {
          setTasks(next);
          setFilter('all');
          setEditing(null);
        });
      },
    );
  }, [ready]);

  useEffect(() => {
    if (!ready) return;
    return registerDesktopVoice(
      () => tasksRef.current,
      () => storageErrorRef.current,
      (next) => {
        flushSync(() => {
          setTasks(next);
          setFilter('all');
          setEditing(null);
          setNotice('已根据你的话更新待办清单');
        });
      },
    );
  }, [ready]);

  function addTask() {
    const text = draft.trim();
    if (!text) {
      input.current?.focus();
      return;
    }
    const task = createTask(text);
    setTasks((previous) => [task, ...previous]);
    setDraft('');
    setFilter(filter === 'done' ? 'active' : filter);
    setNotice('已添加任务');
    input.current?.focus();
  }

  function saveEdit(id: string) {
    if (!editText.trim()) {
      setNotice('任务内容不能为空');
      return;
    }
    setTasks((previous) =>
      previous.map((task) =>
        task.id === id ? { ...task, text: editText.trim() } : task,
      ),
    );
    setEditing(null);
    setNotice('已保存修改');
  }

  function deleteTask(task: Task) {
    setRemoved({ task, index: tasks.findIndex((item) => item.id === task.id) });
    setTasks((previous) => previous.filter((item) => item.id !== task.id));
    if (editing === task.id) setEditing(null);
    setNotice('已删除任务');
  }

  function undoDelete() {
    if (!removed) return;
    setTasks((previous) => {
      const restored = [...previous];
      restored.splice(
        Math.min(removed.index, restored.length),
        0,
        removed.task,
      );
      return restored;
    });
    setRemoved(null);
    setNotice('已恢复任务');
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <a className="brand" href="/" aria-label="一件待办首页">
          <span className="brand-icon">
            <Check strokeWidth={3} size={21} />
          </span>
          <span>
            一件<span className="brand-divider">/</span>
            <span className="brand-description">待办清单</span>
          </span>
        </a>
        <span className="local-badge">
          <span
            className={storageError ? 'status-dot warning' : 'status-dot'}
          />
          {storageError ? '仅当前页面' : '保存在此浏览器'}
        </span>
      </header>
      <main>
        <div className="page-heading">
          <div>
            <div className="eyebrow">
              <span /> ONE THING AT A TIME
            </div>
            <h1>
              我的待办<span className="heading-dot">.</span>
            </h1>
            <p>把想做的事写下来，然后一件件完成。</p>
          </div>
          <div className="date-block">
            <span>新的一天，从一件小事开始</span>
            <time>{date || '今天'}</time>
          </div>
        </div>
        <div className="workspace">
          <section className="task-panel" aria-label="待办清单">
            <form
              className="composer"
              onSubmit={(event) => {
                event.preventDefault();
                addTask();
              }}
            >
              <Plus size={22} className="composer-icon" aria-hidden="true" />
              <input
                ref={input}
                aria-label="新任务内容"
                placeholder="接下来，想完成什么？"
                value={draft}
                onChange={(event) => setDraft(event.target.value)}
                maxLength={500}
                disabled={!ready}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' && event.nativeEvent.isComposing)
                    event.preventDefault();
                }}
              />
              <button
                className="add-button"
                type="submit"
                disabled={!ready || !draft.trim()}
              >
                <span>添加任务</span>
                <ArrowDownLeft size={17} />
              </button>
            </form>
            {storageError && (
              <p className="storage-error" role="alert">
                {storageError}
              </p>
            )}
            <Tabs
              className="task-tabs"
              value={filter}
              onValueChange={(value) => {
                setFilter(value as Filter);
                setEditing(null);
              }}
            >
              <div className="list-toolbar">
                <TabsList
                  variant="line"
                  className="filter-tabs"
                  aria-label="筛选任务"
                >
                  {filters.map((item) => (
                    <TabsTrigger key={item.id} value={item.id}>
                      {item.label}
                      <span className="tab-count">
                        {item.id === 'all'
                          ? tasks.length
                          : item.id === 'active'
                            ? active
                            : done}
                      </span>
                    </TabsTrigger>
                  ))}
                </TabsList>
              </div>
              {filters.map((item) => {
                const visible = tasks.filter(
                  (task) =>
                    item.id === 'all' ||
                    (item.id === 'done' ? task.done : !task.done),
                );
                return (
                  <TabsContent
                    key={item.id}
                    value={item.id}
                    className="task-content"
                  >
                    {visible.length > 0 ? (
                      <ul className="task-list">
                        {visible.map((task) => (
                          <li
                            key={task.id}
                            className={`task-row ${task.done ? 'is-done' : ''}`}
                          >
                            <Checkbox
                              className="task-checkbox"
                              checked={task.done}
                              aria-label={`${task.done ? '标记为未完成' : '完成'}：${task.text}`}
                              onCheckedChange={(checked) => {
                                setTasks((previous) =>
                                  setTaskDone(previous, task.id, !!checked),
                                );
                                setNotice(
                                  checked ? '任务已完成' : '任务已标记为未完成',
                                );
                              }}
                            />
                            {editing === task.id ? (
                              <form
                                className="edit-form"
                                onSubmit={(event) => {
                                  event.preventDefault();
                                  saveEdit(task.id);
                                }}
                              >
                                <input
                                  autoFocus
                                  aria-label="编辑任务内容"
                                  value={editText}
                                  onChange={(event) =>
                                    setEditText(event.target.value)
                                  }
                                  maxLength={500}
                                  onKeyDown={(event) => {
                                    if (event.key === 'Escape')
                                      setEditing(null);
                                    if (
                                      event.key === 'Enter' &&
                                      event.nativeEvent.isComposing
                                    )
                                      event.preventDefault();
                                  }}
                                />
                                <button
                                  type="submit"
                                  className="icon-button save-edit"
                                  aria-label="保存修改"
                                >
                                  <Check size={18} />
                                </button>
                                <button
                                  type="button"
                                  className="icon-button"
                                  aria-label="取消编辑"
                                  onClick={() => setEditing(null)}
                                >
                                  <X size={18} />
                                </button>
                              </form>
                            ) : (
                              <>
                                <span className="task-text">{task.text}</span>
                                <div className="row-actions">
                                  <button
                                    type="button"
                                    className="icon-button"
                                    aria-label={`编辑：${task.text}`}
                                    title="编辑任务"
                                    onClick={() => {
                                      setEditing(task.id);
                                      setEditText(task.text);
                                    }}
                                  >
                                    <Pencil size={17} />
                                  </button>
                                  <button
                                    type="button"
                                    className="icon-button delete-button"
                                    aria-label={`删除：${task.text}`}
                                    title="删除任务"
                                    onClick={() => deleteTask(task)}
                                  >
                                    <Trash2 size={17} />
                                  </button>
                                </div>
                              </>
                            )}
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <Empty className="empty-state">
                        <EmptyHeader>
                          <EmptyMedia className="empty-symbol">
                            {item.id === 'active' && tasks.length ? (
                              <CheckCheck size={32} strokeWidth={1.5} />
                            ) : item.id === 'done' ? (
                              <CircleCheck size={32} strokeWidth={1.5} />
                            ) : (
                              <ListTodo size={32} strokeWidth={1.5} />
                            )}
                          </EmptyMedia>
                          <EmptyTitle className="empty-title">
                            {!ready
                              ? '正在读取清单'
                              : item.id === 'done'
                                ? '每一次完成，都算数'
                                : item.id === 'active' && tasks.length
                                  ? '都完成了，做得不错'
                                  : '从第一件小事开始'}
                          </EmptyTitle>
                          <EmptyDescription>
                            {item.id === 'done'
                              ? '完成的任务会出现在这里。'
                              : item.id === 'active' && tasks.length
                                ? '给自己一点休息的时间吧。'
                                : '在上方写下任务，让想法开始行动。'}
                          </EmptyDescription>
                        </EmptyHeader>
                      </Empty>
                    )}
                  </TabsContent>
                );
              })}
            </Tabs>
            <div className="list-footer">
              <span>
                <Circle size={13} />
                {active > 0
                  ? `还有 ${active} 件事，慢慢来`
                  : tasks.length
                    ? '所有任务已完成'
                    : '你的清单，留给重要的事'}
              </span>
              <span>{tasks.length} 项任务</span>
            </div>
          </section>
          <aside className="side-column">
            <section className="progress-card" aria-label="完成进度">
              <div className="card-heading">
                <span>一点点，向前走</span>
                <ArrowUpRight size={19} />
              </div>
              <div
                className="progress-ring"
                role="progressbar"
                aria-label="任务完成比例"
                aria-valuenow={progress}
                aria-valuemin={0}
                aria-valuemax={100}
                style={{
                  background: `conic-gradient(#4263eb ${progress}%, #e9edf5 0)`,
                }}
              >
                <div>
                  <strong>
                    {progress}
                    <span>%</span>
                  </strong>
                  <span>完成进度</span>
                </div>
              </div>
              <div className="progress-stats">
                <div>
                  <strong>{active.toString().padStart(2, '0')}</strong>
                  <span>
                    <i className="active-dot" />
                    未完成
                  </span>
                </div>
                <div>
                  <strong>{done.toString().padStart(2, '0')}</strong>
                  <span>
                    <i className="done-dot" />
                    已完成
                  </span>
                </div>
              </div>
            </section>
            <section className="tips-card">
              <span className="tips-label">用起来，很简单</span>
              <p>
                <span>添加一件事</span>
                <kbd>Enter ↵</kbd>
              </p>
              <p>
                <span>完成一件事</span>
                <Check size={15} />
              </p>
              <p>
                <span>修改任务内容</span>
                <Pencil size={14} />
              </p>
              <div className="local-note">任务仅保存在当前浏览器中。</div>
            </section>
          </aside>
        </div>
        <footer className="page-footer">
          <span>一件一件，完成就好。</span>
          <span>KEEP IT SIMPLE.</span>
        </footer>
      </main>
      <div className="sr-only" role="status" aria-live="polite">
        {notice}
      </div>
      {removed && (
        <div className="undo-bar" role="status">
          <span>已删除一项任务</span>
          <button type="button" onClick={undoDelete}>
            <RotateCcw size={15} />
            撤销
          </button>
          <button
            type="button"
            aria-label="关闭撤销提示"
            onClick={() => setRemoved(null)}
          >
            <X size={16} />
          </button>
        </div>
      )}
    </div>
  );
}
