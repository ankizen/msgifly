import type {
  AutomationButtonConfig,
  ConditionConfig,
  ConfigFor,
  SendButtonsConfig,
  SendMessageConfig,
  SendTemplateConfig,
  SendWebhookConfig,
  StepNode,
  StepType,
  StopConfig,
  UpdateContactFieldConfig,
  WaitConfig,
  WireStepNode,
} from './types';

function newId(): string {
  return crypto.randomUUID();
}

/** One default config object per step type — values match the old canvas's STEP_DEFS defaults. */
export function defaultConfigFor<T extends StepType>(type: T): ConfigFor<T> {
  switch (type) {
    case 'SendMessage':
      return { text: '' } as ConfigFor<T>;
    case 'SendTemplate':
      return { templateName: '', language: 'en_US', headerParam: null, bodyParams: [] } as unknown as ConfigFor<T>;
    case 'SendButtons':
      return { bodyText: '', buttons: [] } as unknown as ConfigFor<T>;
    case 'Wait':
      return { amount: 1, unit: 'minutes' } as ConfigFor<T>;
    case 'Condition':
      return { subject: 'MessageContent', operand: '', value: '' } as ConfigFor<T>;
    case 'UpdateContactField':
      return { field: 'FirstName', value: '' } as ConfigFor<T>;
    case 'SendWebhook':
      return { url: '', bodyTemplate: '', headers: null } as ConfigFor<T>;
    case 'Stop':
    default:
      return {} as ConfigFor<T>;
  }
}

export function newStep(type: StepType): StepNode {
  if (type === 'Condition') {
    return { id: newId(), type: 'Condition', config: defaultConfigFor('Condition'), yes: [], no: [] };
  }
  return { id: newId(), type, config: defaultConfigFor(type) } as StepNode;
}

/** Recursively assigns fresh client ids to a server-supplied tree — ids never come from the server. */
export function treeFromWire(nodes: WireStepNode[]): StepNode[] {
  return nodes.map((n) => {
    if (n.type === 'Condition') {
      return {
        id: newId(),
        type: 'Condition',
        config: (n.config ?? defaultConfigFor('Condition')) as ConditionConfig,
        yes: treeFromWire(n.yes ?? []),
        no: treeFromWire(n.no ?? []),
      };
    }
    return { id: newId(), type: n.type, config: n.config ?? defaultConfigFor(n.type) } as StepNode;
  });
}

/** Inverse of treeFromWire — strips ids back out, producing exactly AutomationStepNode[]'s shape. */
export function treeToWire(nodes: StepNode[]): WireStepNode[] {
  return nodes.map((n) => {
    if (n.type === 'Condition') {
      return { type: n.type, config: n.config, yes: treeToWire(n.yes), no: treeToWire(n.no) };
    }
    return { type: n.type, config: n.config };
  });
}

export function findNode(steps: StepNode[], id: string): StepNode | null {
  for (const step of steps) {
    if (step.id === id) return step;
    if (step.type === 'Condition') {
      const inYes = findNode(step.yes, id);
      if (inYes) return inYes;
      const inNo = findNode(step.no, id);
      if (inNo) return inNo;
    }
  }
  return null;
}

export function countDescendants(node: StepNode): number {
  if (node.type !== 'Condition') return 0;
  const all = [...node.yes, ...node.no];
  return all.length + all.reduce((sum, child) => sum + countDescendants(child), 0);
}

/** Recursively locates `afterId` wherever it lives (root array or any nested .yes/.no) and splices
 * `newNode` immediately after it in that same array — covers both "append at a dead end" and
 * "insert mid-chain" with one primitive, since either way the anchor's array already holds
 * whatever follows it. Returns a new array (and new arrays along the modified path) for React's
 * immutable-update model; returns the original reference untouched if afterId isn't found here. */
export function insertAfterNode(steps: StepNode[], afterId: string, newNode: StepNode): StepNode[] {
  const index = steps.findIndex((s) => s.id === afterId);
  if (index !== -1) {
    const next = [...steps];
    next.splice(index + 1, 0, newNode);
    return next;
  }

  let changed = false;
  const next = steps.map((step) => {
    if (step.type !== 'Condition') return step;
    const yes = insertAfterNode(step.yes, afterId, newNode);
    const no = insertAfterNode(step.no, afterId, newNode);
    if (yes === step.yes && no === step.no) return step;
    changed = true;
    return { ...step, yes, no };
  });
  return changed ? next : steps;
}

export type InsertScope = { kind: 'root' } | { kind: 'branch'; conditionId: string; branch: 'yes' | 'no' };

/** Unshifts `newNode` to the front of the root array, or a Condition's Yes/No branch — used only
 * where an EmptySlotNode placeholder is currently rendered (root-is-empty, or an empty branch). */
export function insertAtScopeStart(steps: StepNode[], scope: InsertScope, newNode: StepNode): StepNode[] {
  if (scope.kind === 'root') {
    return [newNode, ...steps];
  }

  let changed = false;
  const next = steps.map((step) => {
    if (step.type !== 'Condition') return step;
    if (step.id === scope.conditionId) {
      changed = true;
      return scope.branch === 'yes' ? { ...step, yes: [newNode, ...step.yes] } : { ...step, no: [newNode, ...step.no] };
    }
    const yes = insertAtScopeStart(step.yes, scope, newNode);
    const no = insertAtScopeStart(step.no, scope, newNode);
    if (yes === step.yes && no === step.no) return step;
    changed = true;
    return { ...step, yes, no };
  });
  return changed ? next : steps;
}

/** Splices the node out of its containing array. Cascade-delete (Condition's whole subtree) and
 * reconnect (linear chain closing the gap) both fall out of the splice automatically — the tree
 * has no orphan-tolerant shape, so there's nothing else to special-case. */
export function deleteStep(steps: StepNode[], id: string): { steps: StepNode[]; removedCount: number } {
  const index = steps.findIndex((s) => s.id === id);
  if (index !== -1) {
    const removed = steps[index];
    const removedCount = 1 + countDescendants(removed);
    const next = [...steps];
    next.splice(index, 1);
    return { steps: next, removedCount };
  }

  let removedCount = 0;
  let changed = false;
  const next = steps.map((step) => {
    if (step.type !== 'Condition') return step;
    const yesResult = deleteStep(step.yes, id);
    const noResult = deleteStep(step.no, id);
    if (yesResult.steps === step.yes && noResult.steps === step.no) return step;
    changed = true;
    removedCount = yesResult.removedCount + noResult.removedCount;
    return { ...step, yes: yesResult.steps, no: noResult.steps };
  });
  return changed ? { steps: next, removedCount } : { steps, removedCount: 0 };
}

export function updateStepConfig<T extends StepType>(steps: StepNode[], id: string, patch: Partial<ConfigFor<T>>): StepNode[] {
  let changed = false;
  const next = steps.map((step) => {
    if (step.id === id) {
      changed = true;
      return { ...step, config: { ...step.config, ...patch } } as StepNode;
    }
    if (step.type !== 'Condition') return step;
    const yes = updateStepConfig(step.yes, id, patch);
    const no = updateStepConfig(step.no, id, patch);
    if (yes === step.yes && no === step.no) return step;
    changed = true;
    return { ...step, yes, no };
  });
  return changed ? next : steps;
}

// Re-exported so form components can build a fresh button/config shape without importing types.ts
// directly for the handful that need constructing (e.g. adding a button row).
export type { AutomationButtonConfig, ConditionConfig, SendButtonsConfig, SendMessageConfig, SendTemplateConfig, SendWebhookConfig, StopConfig, UpdateContactFieldConfig, WaitConfig };
