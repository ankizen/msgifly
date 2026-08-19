import type {
  ConditionConfig,
  ConfigFor,
  EmailStepNode,
  EmailStepType,
  ListRefConfig,
  SendEmailConfig,
  StopConfig,
  TagRefConfig,
  UpdateSubscriberFieldConfig,
  WaitConfig,
  WebhookConfig,
  WireStepNode,
} from './types';

function newId(): string {
  return crypto.randomUUID();
}

/** One default config object per step type. */
export function defaultConfigFor<T extends EmailStepType>(type: T): ConfigFor<T> {
  switch (type) {
    case 'SendEmail':
      return { subject: '', bodyHtml: '' } as ConfigFor<T>;
    case 'Wait':
      return { amount: 1, unit: 'minutes' } as ConfigFor<T>;
    case 'Condition':
      return { subject: 'SubscriberField', operand: '', value: '' } as ConfigFor<T>;
    case 'AddTag':
    case 'RemoveTag':
      return { tagId: 0 } as ConfigFor<T>;
    case 'AddToList':
    case 'RemoveFromList':
      return { listId: 0 } as ConfigFor<T>;
    case 'UpdateSubscriberField':
      return { field: 'FirstName', value: '' } as ConfigFor<T>;
    case 'Webhook':
      return { url: '', bodyTemplate: '', headers: undefined } as ConfigFor<T>;
    case 'Stop':
    default:
      return {} as ConfigFor<T>;
  }
}

export function newStep(type: EmailStepType): EmailStepNode {
  if (type === 'Condition') {
    return { id: newId(), type: 'Condition', config: defaultConfigFor('Condition'), yes: [], no: [] };
  }
  return { id: newId(), type, config: defaultConfigFor(type) } as EmailStepNode;
}

/** Recursively assigns fresh client ids to a server-supplied tree — ids never come from the server. */
export function treeFromWire(nodes: WireStepNode[]): EmailStepNode[] {
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
    return { id: newId(), type: n.type, config: n.config ?? defaultConfigFor(n.type) } as EmailStepNode;
  });
}

/** Inverse of treeFromWire — strips ids back out, producing exactly EmailAutomationStepNode[]'s shape. */
export function treeToWire(nodes: EmailStepNode[]): WireStepNode[] {
  return nodes.map((n) => {
    if (n.type === 'Condition') {
      return { type: n.type, config: n.config, yes: treeToWire(n.yes), no: treeToWire(n.no) };
    }
    return { type: n.type, config: n.config };
  });
}

export function findNode(steps: EmailStepNode[], id: string): EmailStepNode | null {
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

export function countDescendants(node: EmailStepNode): number {
  if (node.type !== 'Condition') return 0;
  const all = [...node.yes, ...node.no];
  return all.length + all.reduce((sum, child) => sum + countDescendants(child), 0);
}

/** Recursively locates `afterId` wherever it lives (root array or any nested .yes/.no) and splices
 * `newNode` immediately after it in that same array. Returns a new array (and new arrays along the
 * modified path) for React's immutable-update model; returns the original reference untouched if
 * afterId isn't found here. */
export function insertAfterNode(steps: EmailStepNode[], afterId: string, newNode: EmailStepNode): EmailStepNode[] {
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

/** Where a pick from AddStepDrawer should land: right after an existing step (the per-row "+"
 * connector), or at the front of an empty list/branch (the dashed placeholder button) — the two
 * insertion primitives insertAfterNode/insertAtScopeStart below cover both. */
export type AddTarget = { kind: 'after'; anchorId: string } | { kind: 'start'; scope: InsertScope };

/** Unshifts `newNode` to the front of the root array, or a Condition's Yes/No branch — used only
 * where StepList's own "+ Add step" empty-state placeholder is currently rendered (root-is-empty,
 * or an empty branch). */
export function insertAtScopeStart(steps: EmailStepNode[], scope: InsertScope, newNode: EmailStepNode): EmailStepNode[] {
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
export function deleteStep(steps: EmailStepNode[], id: string): { steps: EmailStepNode[]; removedCount: number } {
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

/** Deep-clones a node with fresh ids throughout its subtree — same re-id need as treeFromWire
 * (a clone can never share an id with its source), just applied to an in-memory node instead of a
 * wire-parsed one. */
function regenerateIds(node: EmailStepNode): EmailStepNode {
  if (node.type === 'Condition') {
    return { ...node, id: newId(), yes: node.yes.map(regenerateIds), no: node.no.map(regenerateIds) };
  }
  return { ...node, id: newId() };
}

/** Clones the node (and, for a Condition, its whole Yes/No subtree) and splices the clone in
 * immediately after the original — mirrors the FluentCRM funnel editor's per-block "Clone" menu
 * action. Same recursive-array-hunt shape as deleteStep/updateStepConfig below. */
export function cloneStep(steps: EmailStepNode[], id: string): EmailStepNode[] {
  const index = steps.findIndex((s) => s.id === id);
  if (index !== -1) {
    const next = [...steps];
    next.splice(index + 1, 0, regenerateIds(steps[index]));
    return next;
  }

  let changed = false;
  const next = steps.map((step) => {
    if (step.type !== 'Condition') return step;
    const yes = cloneStep(step.yes, id);
    const no = cloneStep(step.no, id);
    if (yes === step.yes && no === step.no) return step;
    changed = true;
    return { ...step, yes, no };
  });
  return changed ? next : steps;
}

/** Swaps the node with its immediate previous/next sibling in whichever array it lives in — the
 * up/down reorder buttons on each step card. A no-op (returns the original reference) at either
 * end of the list, same as FluentCRM's disabled-at-the-ends arrow buttons. */
export function moveStep(steps: EmailStepNode[], id: string, direction: 'up' | 'down'): EmailStepNode[] {
  const index = steps.findIndex((s) => s.id === id);
  if (index !== -1) {
    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= steps.length) return steps;
    const next = [...steps];
    [next[index], next[targetIndex]] = [next[targetIndex], next[index]];
    return next;
  }

  let changed = false;
  const next = steps.map((step) => {
    if (step.type !== 'Condition') return step;
    const yes = moveStep(step.yes, id, direction);
    const no = moveStep(step.no, id, direction);
    if (yes === step.yes && no === step.no) return step;
    changed = true;
    return { ...step, yes, no };
  });
  return changed ? next : steps;
}

export function updateStepConfig<T extends EmailStepType>(steps: EmailStepNode[], id: string, patch: Partial<ConfigFor<T>>): EmailStepNode[] {
  let changed = false;
  const next = steps.map((step) => {
    if (step.id === id) {
      changed = true;
      return { ...step, config: { ...step.config, ...patch } } as EmailStepNode;
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

// Re-exported so form components can build a fresh config shape without importing types.ts directly.
export type { ConditionConfig, ListRefConfig, SendEmailConfig, StopConfig, TagRefConfig, UpdateSubscriberFieldConfig, WaitConfig, WebhookConfig };
