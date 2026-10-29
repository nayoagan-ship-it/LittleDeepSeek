export type Task = { id: string; text: string; done: boolean };
export type Filter = 'all' | 'active' | 'done';
export const STORAGE_KEY = 'yijian-todos-v1';

export function createTask(text: unknown): Task {
  if (typeof text !== 'string' || !text.trim() || text.trim().length > 500) {
    throw new Error('任务内容需要为 1 至 500 个字符');
  }
  return { id: crypto.randomUUID(), text: text.trim(), done: false };
}

export function parseTasks(raw: string): Task[] {
  const stored: unknown = JSON.parse(raw);
  if (
    !Array.isArray(stored) ||
    !stored.every(
      (task) =>
        task &&
        typeof task.id === 'string' &&
        typeof task.text === 'string' &&
        task.text.trim() &&
        typeof task.done === 'boolean',
    ) ||
    new Set(stored.map((task) => task.id)).size !== stored.length
  ) {
    throw new Error('已保存的任务数据无效');
  }
  return stored;
}

export function setTaskDone(tasks: Task[], id: string, done: boolean): Task[] {
  if (!tasks.some((task) => task.id === id)) throw new Error('未找到该任务');
  return tasks.map((task) => (task.id === id ? { ...task, done } : task));
}
