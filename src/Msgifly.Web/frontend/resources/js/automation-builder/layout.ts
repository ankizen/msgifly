import dagre from '@dagrejs/dagre';

export interface LayoutNode {
  id: string;
  width: number;
  height: number;
}

export interface LayoutEdge {
  source: string;
  target: string;
}

export interface Point {
  x: number;
  y: number;
}

// Top-to-bottom, tree-branches style: each step stacks downward, and a Condition's Yes/No
// branches fan out left/right beneath it rather than offsetting in y while continuing rightward.
// nodesep is now the HORIZONTAL gap between sibling branch columns (wider, since two branch
// columns sitting close together read as cramped); ranksep is the VERTICAL gap between successive
// steps (tighter than the old LR ranksep, since these cards are short).
function runDagre(nodes: LayoutNode[], edges: LayoutEdge[]): Map<string, Point> {
  const g = new dagre.graphlib.Graph();
  g.setGraph({ rankdir: 'TB', nodesep: 90, ranksep: 70 });
  g.setDefaultEdgeLabel(() => ({}));

  for (const n of nodes) {
    g.setNode(n.id, { width: n.width, height: n.height });
  }
  for (const e of edges) {
    // dagre silently no-ops setEdge for an endpoint it doesn't know about — guard so a stray edge
    // referencing a not-yet-rendered placeholder id can't throw.
    if (g.hasNode(e.source) && g.hasNode(e.target)) {
      g.setEdge(e.source, e.target);
    }
  }

  dagre.layout(g);

  const positions = new Map<string, Point>();
  for (const n of nodes) {
    const { x, y } = g.node(n.id);
    // dagre positions are node-center — React Flow positions are top-left.
    positions.set(n.id, { x: x - n.width / 2, y: y - n.height / 2 });
  }
  return positions;
}

/** Runs dagre over the whole current graph (every node needs the full structure to be positioned
 * sensibly relative to its neighbors) but returns positions only for ids NOT already present in
 * `knownPositions` — the caller merges these in, leaving anything already positioned (including
 * anything the user manually dragged) untouched. */
export function computeMissingPositions(nodes: LayoutNode[], edges: LayoutEdge[], knownPositions: Map<string, Point>): Map<string, Point> {
  const missingIds = new Set(nodes.filter((n) => !knownPositions.has(n.id)).map((n) => n.id));
  if (missingIds.size === 0) return new Map();

  const all = runDagre(nodes, edges);
  const result = new Map<string, Point>();
  for (const id of missingIds) {
    const pos = all.get(id);
    if (pos) result.set(id, pos);
  }
  return result;
}

/** Re-runs dagre for every currently-visible node, ignoring any prior manual positioning — the
 * deliberate "Tidy up" escape hatch. */
export function tidyLayout(nodes: LayoutNode[], edges: LayoutEdge[]): Map<string, Point> {
  return runDagre(nodes, edges);
}
