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

// Left-to-right, n8n-style pipeline: each step sits in its own column reading left to right, and a
// Condition's Yes/No branches fan out vertically (stacked rows) on the right edge while continuing
// rightward. nodesep is the VERTICAL gap between sibling branch rows; ranksep is the HORIZONTAL gap
// between successive columns. Identical to the WhatsApp canvas's layout.ts — this module has no
// project-specific types at all, so it's copied verbatim rather than adapted.
function runDagre(nodes: LayoutNode[], edges: LayoutEdge[]): Map<string, Point> {
  const g = new dagre.graphlib.Graph();
  g.setGraph({ rankdir: 'LR', nodesep: 60, ranksep: 100 });
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

/** Re-runs dagre for every currently-visible node, ignoring any prior manual positioning. Used for
 * both the initial load AND every structural tree edit (a step added or removed), plus the
 * deliberate "Tidy up" escape hatch — see EmailAutomationBuilderApp's layout effect for why a full
 * re-layout on every structural change (rather than only positioning the new/removed node) is the
 * right call. */
export function tidyLayout(nodes: LayoutNode[], edges: LayoutEdge[]): Map<string, Point> {
  return runDagre(nodes, edges);
}
