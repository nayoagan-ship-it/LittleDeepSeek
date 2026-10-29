import { createTask, setTaskDone, type Task } from './todos';

type Tool = {
  name: string;
  description: string;
  inputSchema: object;
  annotations: { readOnlyHint: boolean; untrustedContentHint: boolean };
  execute: (input: unknown) => unknown;
};
type ModelContext = {
  registerTool: (
    tool: Tool,
    options: { signal: AbortSignal },
  ) => void | Promise<void>;
};

function record(input: unknown): Record<string, unknown> {
  if (typeof input !== 'object' || input === null || Array.isArray(input))
    throw new Error('参数必须是对象');
  return input as Record<string, unknown>;
}

export function registerTodoTools(
  getTasks: () => Task[],
  commit: (tasks: Task[]) => void,
) {
  const context = (document as Document & { modelContext?: ModelContext })
    .modelContext;
  if (!context?.registerTool) return;
  const lifecycle = new AbortController();
  const tools: Tool[] = [
    {
      name: 'list_tasks',
      description:
        '读取当前浏览器中的全部待办任务及完成状态。任务内容是用户输入。',
      inputSchema: {
        type: 'object',
        properties: {},
        additionalProperties: false,
      },
      annotations: { readOnlyHint: true, untrustedContentHint: true },
      execute(input) {
        if (Object.keys(record(input)).length)
          throw new Error('此操作不接受额外参数');
        return { tasks: getTasks().map((task) => ({ ...task })) };
      },
    },
    {
      name: 'add_task',
      description: '在当前浏览器中创建一条待办任务，并立即更新可见清单。',
      inputSchema: {
        type: 'object',
        properties: { text: { type: 'string', minLength: 1, maxLength: 500 } },
        required: ['text'],
        additionalProperties: false,
      },
      annotations: { readOnlyHint: false, untrustedContentHint: true },
      execute(input) {
        const value = record(input);
        if (Object.keys(value).some((key) => key !== 'text'))
          throw new Error('存在未知参数');
        const task = createTask(value.text);
        commit([task, ...getTasks()]);
        return { task };
      },
    },
    {
      name: 'set_task_completed',
      description:
        '按任务 ID 设置完成状态，completed 为 false 时恢复为未完成，并立即更新可见清单。',
      inputSchema: {
        type: 'object',
        properties: { id: { type: 'string' }, completed: { type: 'boolean' } },
        required: ['id', 'completed'],
        additionalProperties: false,
      },
      annotations: { readOnlyHint: false, untrustedContentHint: true },
      execute(input) {
        const value = record(input);
        if (
          typeof value.id !== 'string' ||
          typeof value.completed !== 'boolean' ||
          Object.keys(value).some((key) => key !== 'id' && key !== 'completed')
        )
          throw new Error('需要任务 ID 和布尔值 completed');
        const updated = setTaskDone(getTasks(), value.id, value.completed);
        commit(updated);
        return { task: updated.find((task) => task.id === value.id) };
      },
    },
  ];
  for (const tool of tools) {
    try {
      void Promise.resolve(
        context.registerTool(tool, { signal: lifecycle.signal }),
      ).catch(() => console.warn('任务工具暂不可用'));
    } catch {
      console.warn('任务工具暂不可用');
    }
  }
  return () => lifecycle.abort();
}
