import { MarkerType, type Edge, type Node } from '@xyflow/react';
import type { InsertScope } from './tree';
import type { EmailAutomationTree, EmailStepNode } from './types';

export const TRIGGER_NODE_ID = '__trigger__';

// Static, generous estimates rather than live DOM measurement — see the WhatsApp canvas's
// derive-graph.ts for the full reasoning (a real card's height varies by content; dagre only needs
// a starting guess, dragging fixes any card taller than this in practice). The SAME NODE_WIDTH is
// imported by every node component's own inline style, so the DOM card and dagre's reserved column
// width can never drift apart.
export const NODE_WIDTH = 260;
export const NODE_HEIGHT = 120;
export const TRIGGER_NODE_HEIGHT = 110;
export const EMPTY_SLOT_HEIGHT = 56;

export interface TriggerNodeData extends Record<string, unknown> {
  kind: 'trigger';
  triggerType: string;
}
export interface StepNodeData extends Record<string, unknown> {
  kind: 'step';
  step: EmailStepNode;
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

const DEFAULT_EDGE_COLOR = '#94a3b8';

function makeEdge(source: string, target: string, style: EdgeStyle | undefined): Edge {
  const color = style?.color ?? DEFAULT_EDGE_COLOR;
  return {
    id: `${source}->${target}`,
    source,
    // A Condition node exposes two named source handles ('yes'/'no' — see StepNode.tsx); every
    // other node type has one unnamed default handle, so sourceHandle is only set when relevant.
    sourceHandle: style ? style.label.toLowerCase() : undefined,
    target,
    type: 'smoothstep',
    label: style?.label,
    style: { stroke: color, strokeWidth: 2 },
    labelStyle: style ? { fill: style.color, fontWeight: 600, fontSize: 11 } : undefined,
    labelBgStyle: style ? { fill: '#ffffff', fillOpacity: 0.9 } : undefined,
    labelBgPadding: style ? [4, 2] : undefined,
    markerEnd: { type: MarkerType.ArrowClosed, color, width: 16, height: 16 },
  };
}

/** Walks the tree into React Flow nodes/edges. Positions are left at (0,0) here — the caller
 * (EmailAutomationBuilderApp) merges in the local positions map (dagre-computed or user-dragged)
 * afterward, since position is never part of this derivation's own concern (it isn't part of the
 * canonical tree at all). A Condition always terminates its array's rendering (only Yes/No
 * continue) — validation.ts is responsible for surfacing any trailing-sibling shape this can't
 * render; this function just doesn't render what it structurally can't. */
export function deriveGraph(tree: EmailAutomationTree): { nodes: BuilderNode[]; edges: Edge[] } {
  const nodes: BuilderNode[] = [
    {
      id: TRIGGER_NODE_ID,
      type: 'trigger',
      position: { x: 0, y: 0 },
      data: { kind: 'trigger', triggerType: tree.triggerType },
    },
  ];
  const edges: Edge[] = [];

  function walkChain(steps: EmailStepNode[], fromId: string, edgeStyle: EdgeStyle | undefined) {
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
