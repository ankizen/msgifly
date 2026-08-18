import type { Edge, Node } from '@xyflow/react';
import type { InsertScope } from './tree';
import type { AutomationTree, StepNode } from './types';

export const TRIGGER_NODE_ID = '__trigger__';

// Static, generous estimates rather than live DOM measurement — a real card's height varies a lot
// by step type/content (a SendTemplate card with a header-image preview can be much taller than a
// plain Wait card), and an unloaded image's real box height isn't known until it finishes loading
// anyway. Same reasoning the old canvas used for its own static CONDITION_BRANCH_OFFSETS. These
// only seed dagre's initial guess — dragging is always available to fix any card that's taller
// than this in practice.
export const NODE_WIDTH = 260;
export const NODE_HEIGHT = 170;
export const TRIGGER_NODE_HEIGHT = 230;
export const EMPTY_SLOT_HEIGHT = 64;

export interface TriggerNodeData extends Record<string, unknown> {
  kind: 'trigger';
  triggerType: string;
}
export interface StepNodeData extends Record<string, unknown> {
  kind: 'step';
  step: StepNode;
}
export interface EmptySlotNodeData extends Record<string, unknown> {
  kind: 'emptySlot';
  scope: InsertScope;
  label: 'Yes' | 'No';
}

export type BuilderNode = Node<TriggerNodeData | StepNodeData | EmptySlotNodeData>;

interface EdgeStyle {
  label: 'Yes' | 'No';
  color: string;
}

function makeEdge(source: string, target: string, style: EdgeStyle | undefined): Edge {
  return {
    id: `${source}->${target}`,
    source,
    // A Condition node exposes two named source handles ('yes'/'no' — see StepNode.tsx); every
    // other node type has one unnamed default handle, so sourceHandle is only set when relevant.
    sourceHandle: style ? style.label.toLowerCase() : undefined,
    target,
    label: style?.label,
    style: style ? { stroke: style.color } : { stroke: '#94a3b8' },
    labelStyle: style ? { fill: style.color, fontWeight: 600, fontSize: 11 } : undefined,
  };
}

/** Walks the tree into React Flow nodes/edges. Positions are left at (0,0) here — the caller
 * (AutomationBuilderApp) merges in the local positions map (dagre-computed or user-dragged)
 * afterward, since position is never part of this derivation's own concern (it isn't part of the
 * canonical tree at all). Mirrors old canvas's layoutChain(): a Condition always terminates its
 * array's rendering (only Yes/No continue) — any trailing sibling after a Condition (a shape only
 * an externally-authored tree, e.g. via MCP, could produce) has no canvas representation here
 * either, exactly matching prior behavior. validation.ts is responsible for detecting and
 * surfacing that case as a notice; this function just doesn't render what it structurally can't. */
export function deriveGraph(tree: AutomationTree): { nodes: BuilderNode[]; edges: Edge[] } {
  const nodes: BuilderNode[] = [
    {
      id: TRIGGER_NODE_ID,
      type: 'trigger',
      position: { x: 0, y: 0 },
      data: { kind: 'trigger', triggerType: tree.triggerType },
    },
  ];
  const edges: Edge[] = [];

  function walkChain(steps: StepNode[], fromId: string, edgeStyle: EdgeStyle | undefined) {
    let prevId = fromId;
    let nextEdgeStyle = edgeStyle;

    for (const step of steps) {
      nodes.push({ id: step.id, type: 'step', position: { x: 0, y: 0 }, data: { kind: 'step', step } });
      edges.push(makeEdge(prevId, step.id, nextEdgeStyle));
      nextEdgeStyle = undefined; // only the first edge into a chain carries the branch's Yes/No style

      if (step.type === 'Condition') {
        if (step.yes.length === 0) {
          const slotId = `empty:${step.id}:yes`;
          nodes.push({ id: slotId, type: 'emptySlot', position: { x: 0, y: 0 }, data: { kind: 'emptySlot', scope: { kind: 'branch', conditionId: step.id, branch: 'yes' }, label: 'Yes' } });
          edges.push(makeEdge(step.id, slotId, { label: 'Yes', color: '#16a34a' }));
        } else {
          walkChain(step.yes, step.id, { label: 'Yes', color: '#16a34a' });
        }

        if (step.no.length === 0) {
          const slotId = `empty:${step.id}:no`;
          nodes.push({ id: slotId, type: 'emptySlot', position: { x: 0, y: 0 }, data: { kind: 'emptySlot', scope: { kind: 'branch', conditionId: step.id, branch: 'no' }, label: 'No' } });
          edges.push(makeEdge(step.id, slotId, { label: 'No', color: '#dc2626' }));
        } else {
          walkChain(step.no, step.id, { label: 'No', color: '#dc2626' });
        }

        return; // a Condition consumes both its outputs — nothing further in this array renders
      }

      prevId = step.id;
    }
  }

  if (tree.steps.length > 0) {
    walkChain(tree.steps, TRIGGER_NODE_ID, undefined);
  }

  return { nodes, edges };
}

export function heightFor(node: BuilderNode): number {
  if (node.type === 'trigger') return TRIGGER_NODE_HEIGHT;
  if (node.type === 'emptySlot') return EMPTY_SLOT_HEIGHT;
  return NODE_HEIGHT;
}
