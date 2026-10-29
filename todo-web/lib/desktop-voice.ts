import { createTask, setTaskDone, STORAGE_KEY, type Task } from './todos';

type VoiceOperation = { action: 'add' | 'complete'; text?: string; id?: string };
type VoiceBridge = {
  snapshot: () => { tasks: Task[]; storageError: string };
  apply: (operations: VoiceOperation[], expected?: Task[]) =>
    { ok: boolean; error?: string; tasks?: Task[] };
};

export function registerDesktopVoice(
  getTasks: () => Task[],
  getStorageError: () => string,
  commit: (tasks: Task[]) => void,
) {
  const host = window as Window & {
    chrome?: { webview?: unknown };
    littleDeepSeekVoice?: VoiceBridge;
  };
  // Expose mutations only in the pet's packaged, local WebView2 origin.
  if (location.origin !== 'https://todo.littledeepseek.example' || !host.chrome?.webview)
    return;
  const bridge: VoiceBridge = {
    snapshot: () => ({
      tasks: getTasks().map(task => ({ ...task })),
      storageError: getStorageError(),
    }),
    apply(operations, expected) {
      try {
        if (getStorageError()) throw new Error('清单暂时无法保存，请先恢复存储后重试。');
        if (!Array.isArray(operations) || !operations.length || operations.length > 30)
          throw new Error('没有可执行的待办操作。');
        const current = getTasks();
        if (expected && (!Array.isArray(expected) || current.length !== expected.length ||
          current.some((task, index) => task.id !== expected[index]?.id ||
            task.text !== expected[index]?.text || task.done !== expected[index]?.done)))
          throw new Error('清单刚刚发生了变化，请再说一次。');
        let next = current.map(task => ({ ...task }));
        for (const operation of operations) {
          if (!operation || typeof operation !== 'object')
            throw new Error('待办操作无效。');
          if (operation.action === 'add') {
            const task = createTask(operation.text);
            if (!next.some(item => item.text === task.text && !item.done)) next = [task, ...next];
          } else if (operation.action === 'complete' && typeof operation.id === 'string') {
            next = setTaskDone(next, operation.id, true);
          } else throw new Error('不支持这项待办操作。');
        }
        // A failed write must never be reported as a successfully saved voice command.
        localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
        commit(next);
        return { ok: true, tasks: next.map(task => ({ ...task })) };
      } catch (error) {
        return { ok: false, error: error instanceof Error ? error.message : '更新清单失败，请重试。' };
      }
    },
  };
  host.littleDeepSeekVoice = bridge;
  return () => {
    if (host.littleDeepSeekVoice === bridge) delete host.littleDeepSeekVoice;
  };
}
