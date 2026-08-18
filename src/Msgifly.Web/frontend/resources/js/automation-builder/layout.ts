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
// rightward — matching n8n's own flow direction and how an IF node's two outputs branch there,
// rather than a top-to-bottom tree/org-chart shape. nodesep is the VERTICAL gap between sibling
// branch rows; ranksep is the HORIZONTAL gap between successive columns.
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
 * both the initial load AND every structural tree edit (a step added or removed) — see
 * AutomationBuilderApp's layout effect for why a full re-layout on every structural change, rather
 * than only positioning the new/removed node, turned out to be the right call: computing just the
 * new node's position from a fresh whole-graph dagre run while leaving siblings at their old
 * (pre-edit) coordinates let the new node's position and its neighbors' actual on-screen positions
 * drift out of sync — dagre's fresh run assumes everyone reflows together, so cherry-picking one
 * node's answer out of that run while ignoring the rest is exactly what produced the reported
 * "newly added step lands hidden behind an existing one" bug. Also the deliberate "Tidy up"
 * escape hatch. */
export function tidyLayout(nodes: LayoutNode[], edges: LayoutEdge[]): Map<string, Point> {
  return runDagre(nodes, edges);
}
