import { forwardRef, useCallback, useEffect, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { Background, BackgroundVariant, Controls, MiniMap, ReactFlow, type Edge, type NodeMouseHandler, type OnNodeDrag, type ReactFlowInstance } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { BuilderActionsContext, type BuilderActions } from '../builder-context';
import { deriveGraph, heightFor, NODE_WIDTH, TRIGGER_NODE_ID, type BuilderNode, type StepNodeData } from '../derive-graph';
import { tidyLayout, type Point } from '../layout';
import type { InsertScope } from '../tree';
import { countDescendants, deleteStep, findNode, insertAfterNode, insertAtScopeStart, newStep, treeFromWire, treeToWire, updateStepConfig } from '../tree';
import { computeValidationIssues, detectTrailingSiblingsAfterCondition } from '../validation';
import { STEP_COLOR, TRIGGER_COLOR } from '../step-meta';
import type { EmailAutomationBuilderHandle, EmailAutomationTree, EmailStepType, ExportResult, InitialProps, OnChangeState } from '../types';
import { AddStepMenu } from './AddStepMenu';
import { ConfirmDialog } from './ConfirmDialog';
import { PropertiesPanel } from './PropertiesPanel';
import { Toolbar } from './Toolbar';
import { StepNodeCard } from './StepNode';
import { TriggerNodeCard } from './TriggerNode';
import { EmptySlotNodeCard } from './EmptySlotNode';

const NODE_TYPES = { trigger: TriggerNodeCard, step: StepNodeCard, emptySlot: EmptySlotNodeCard };

function minimapColor(node: BuilderNode): string {
  if (node.type === 'trigger') return TRIGGER_COLOR.accent;
  if (node.type === 'step') return STEP_COLOR[(node.data as StepNodeData).step.type].accent;
  return '#cbd5e1';
}

type PendingTarget = { kind: 'root' } | { kind: 'after'; anchorId: string } | { kind: 'slot'; scope: InsertScope };
interface PendingState {
  screenPos: { x: number; y: number };
  target: PendingTarget;
}

interface Props {
  initial: InitialProps;
  onChange: (state: OnChangeState) => void;
}

function buildInitialTree(initial: InitialProps): EmailAutomationTree {
  return {
    triggerType: initial.triggerType || 'SubscriberAdded',
    listId: initial.listId ?? null,
    tagId: initial.tagId ?? null,
    steps: treeFromWire(initial.steps ?? []),
  };
}

/** Independent copy of the WhatsApp canvas's AutomationBuilderApp — same tree-state/positions-map/
 * selection architecture (see that file for the detailed reasoning behind each piece: why layout is
 * local-only state recomputed on every structural change, why the fitView/focus effects gate on
 * rfReady, etc.), adapted to Email's own tree shape and trigger vocabulary. No session-window
 * warning: that's a WhatsApp-specific 24h-messaging-window constraint with no Email analogue. */
export const EmailAutomationBuilderApp = forwardRef<EmailAutomationBuilderHandle, Props>(function EmailAutomationBuilderApp({ initial, onChange }, ref) {
  const [tree, setTree] = useState<EmailAutomationTree>(() => buildInitialTree(initial));
  const [positions, setPositions] = useState<Map<string, Point>>(new Map());
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [pending, setPending] = useState<PendingState | null>(null);
  const [pendingDelete, setPendingDelete] = useState<{ id: string; message: string } | null>(null);
  const rfInstance = useRef<ReactFlowInstance<BuilderNode, Edge> | null>(null);
  // A ref alone isn't enough to gate the auto-fitView effect below — see the WhatsApp canvas's
  // AutomationBuilderApp for why this is mirrored into state.
  const [rfReady, setRfReady] = useState(false);
  const hasRenderedOnce = useRef(false);
  const hasFitOnce = useRef(false);
  const pendingFocusId = useRef<string | null>(null);
  const prevNodeIdsRef = useRef<Set<string>>(new Set());

  // Detected once, off the tree exactly as loaded — this shape can only ever arrive from outside
  // this builder, never be produced by it, so there's no need to recheck after every edit.
  const trailingSiblingIssues = useMemo(() => detectTrailingSiblingsAfterCondition(treeFromWire(initial.steps ?? [])), [initial]);

  const { nodes: baseNodes, edges } = useMemo(() => deriveGraph(tree), [tree]);

  // Positions are local-only state, deliberately outside the canonical tree (the wire contract has
  // no position field at all). Full re-layout on every STRUCTURAL change (a step added or removed),
  // not just a position for the new/removed node — see the WhatsApp canvas's identical effect for
  // why that's the right call.
  useLayoutEffect(() => {
    const currentIds = new Set(baseNodes.map((n) => n.id));
    const prevIds = prevNodeIdsRef.current;
    const structureChanged = currentIds.size !== prevIds.size || [...currentIds].some((id) => !prevIds.has(id));
    if (structureChanged) {
      const layoutNodes = baseNodes.map((n) => ({ id: n.id, width: NODE_WIDTH, height: heightFor(n) }));
      setPositions(tidyLayout(layoutNodes, edges));
    }
    prevNodeIdsRef.current = currentIds;
  }, [baseNodes, edges]);

  const nodes: BuilderNode[] = useMemo(
    () => baseNodes.map((n) => ({ ...n, position: positions.get(n.id) ?? { x: -9999, y: -9999 } })),
    [baseNodes, positions]
  );

  useEffect(() => {
    if (!rfInstance.current) return;
    if (!hasFitOnce.current && nodes.length > 0 && nodes.every((n) => n.position.x !== -9999)) {
      hasFitOnce.current = true;
      rfInstance.current.fitView({ padding: 0.2, duration: 0 });
    }
    if (pendingFocusId.current) {
      const pos = positions.get(pendingFocusId.current);
      if (pos) {
        rfInstance.current.setCenter(pos.x + NODE_WIDTH / 2, pos.y + 60, { zoom: 1, duration: 300 });
        pendingFocusId.current = null;
      }
    }
  }, [nodes, positions, rfReady]);

  const selectedStep = useMemo(() => (selectedId && selectedId !== TRIGGER_NODE_ID ? findNode(tree.steps, selectedId) : null), [tree.steps, selectedId]);

  useEffect(() => {
    onChange({
      validationIssues: [...computeValidationIssues(tree), ...trailingSiblingIssues],
      isInitialRender: !hasRenderedOnce.current,
    });
    hasRenderedOnce.current = true;
    // onChange/trailingSiblingIssues excluded: onChange is the caller's stable callback, and
    // trailingSiblingIssues never changes after mount — only `tree` edits should trigger a recompute.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tree]);

  const onNodeDragStop: OnNodeDrag = useCallback((_event, node) => {
    setPositions((prev) => {
      const next = new Map(prev);
      next.set(node.id, node.position);
      return next;
    });
  }, []);

  const onNodeClick: NodeMouseHandler = useCallback((_event, node) => {
    setSelectedId(node.id);
  }, []);

  const onDelete = useCallback(
    (id: string) => {
      const node = findNode(tree.steps, id);
      if (!node) return;
      const count = countDescendants(node);
      const message =
        node.type === 'Condition' && count > 0
          ? `Delete this step and the ${count} step${count === 1 ? '' : 's'} in its Yes/No branches below it? This can't be undone.`
          : "Delete this step? This can't be undone.";
      setPendingDelete({ id, message });
    },
    [tree.steps]
  );

  function confirmDelete() {
    if (!pendingDelete) return;
    const { id } = pendingDelete;
    const result = deleteStep(tree.steps, id);
    setTree((t) => ({ ...t, steps: result.steps }));
    setPositions((prev) => {
      const next = new Map(prev);
      next.delete(id);
      return next;
    });
    if (selectedId === id) setSelectedId(null);
    setPendingDelete(null);
  }

  const actions: BuilderActions = useMemo(
    () => ({
      selectedId,
      onSelectNode: setSelectedId,
      onAddAfter: (anchorId, event) => {
        event.stopPropagation();
        const target: PendingTarget = anchorId === TRIGGER_NODE_ID ? { kind: 'root' } : { kind: 'after', anchorId };
        setPending({ screenPos: { x: event.clientX, y: event.clientY }, target });
      },
      onAddAtSlot: (scope, event) => {
        event.stopPropagation();
        setPending({ screenPos: { x: event.clientX, y: event.clientY }, target: { kind: 'slot', scope } });
      },
      onDelete,
    }),
    [selectedId, onDelete]
  );

  function handlePick(type: EmailStepType) {
    if (!pending) return;
    const node = newStep(type);
    setTree((t) => {
      let steps;
      if (pending.target.kind === 'root') steps = insertAtScopeStart(t.steps, { kind: 'root' }, node);
      else if (pending.target.kind === 'after') steps = insertAfterNode(t.steps, pending.target.anchorId, node);
      else steps = insertAtScopeStart(t.steps, pending.target.scope, node);
      return { ...t, steps };
    });
    setPending(null);
    setSelectedId(node.id);
    pendingFocusId.current = node.id;
  }

  // Escape clears the current selection; Delete/Backspace deletes the selected step. Both skip
  // while focus is inside a text input/textarea/select so typing in the properties panel never
  // gets misread as a canvas-level shortcut. The Trigger can't be deleted this way.
  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      const tag = (e.target as HTMLElement | null)?.tagName;
      const typing = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
      if (typing) return;
      if (e.key === 'Escape' && selectedId) setSelectedId(null);
      if ((e.key === 'Delete' || e.key === 'Backspace') && selectedId && selectedId !== TRIGGER_NODE_ID) {
        onDelete(selectedId);
      }
    }
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [selectedId, onDelete]);

  useImperativeHandle(
    ref,
    (): EmailAutomationBuilderHandle => ({
      exportForSubmit(): ExportResult {
        return {
          triggerType: tree.triggerType,
          listId: tree.listId,
          tagId: tree.tagId,
          stepsJson: JSON.stringify(treeToWire(tree.steps)),
        };
      },
      focusNode(nodeId: string) {
        const pos = positions.get(nodeId);
        if (pos && rfInstance.current) {
          rfInstance.current.setCenter(pos.x + NODE_WIDTH / 2, pos.y + 60, { zoom: 1, duration: 400 });
        }
        setSelectedId(nodeId);
      },
      tidyUp() {
        const layoutNodes = baseNodes.map((n) => ({ id: n.id, width: NODE_WIDTH, height: heightFor(n) }));
        setPositions(tidyLayout(layoutNodes, edges));
      },
      zoomIn() {
        rfInstance.current?.zoomIn();
      },
      zoomOut() {
        rfInstance.current?.zoomOut();
      },
      zoomReset() {
        rfInstance.current?.fitView();
      },
      destroy() {
        rfInstance.current = null;
      },
    }),
    [tree, positions, baseNodes, edges]
  );

  return (
    <BuilderActionsContext.Provider value={actions}>
      <div className="relative w-full h-full">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={NODE_TYPES}
          onInit={(instance) => {
            rfInstance.current = instance;
            setRfReady(true);
          }}
          onNodeDragStop={onNodeDragStop}
          onNodeClick={onNodeClick}
          onPaneClick={() => setSelectedId(null)}
          nodesConnectable={false}
          edgesReconnectable={false}
          minZoom={0.3}
          maxZoom={1.6}
          proOptions={{ hideAttribution: true }}
        >
          <Background variant={BackgroundVariant.Dots} gap={20} size={1.5} color="#cbd5e1" />
          <Controls showInteractive={false} className="!shadow-sm !border !border-slate-200 dark:!border-slate-600 [&>button]:dark:!bg-slate-800 [&>button]:dark:!fill-slate-300 [&>button]:dark:!border-slate-600" />
          <MiniMap pannable zoomable nodeColor={minimapColor} maskColor="rgba(148, 163, 184, 0.15)" className="!bg-white dark:!bg-slate-800 !border !border-slate-200 dark:!border-slate-600 !shadow-sm" />
        </ReactFlow>

        <Toolbar
          onTidyUp={() => {
            const layoutNodes = baseNodes.map((n) => ({ id: n.id, width: NODE_WIDTH, height: heightFor(n) }));
            setPositions(tidyLayout(layoutNodes, edges));
          }}
          onZoomIn={() => rfInstance.current?.zoomIn()}
          onZoomOut={() => rfInstance.current?.zoomOut()}
          onZoomReset={() => rfInstance.current?.fitView()}
        />

        <PropertiesPanel
          selectedId={selectedId}
          tree={tree}
          selectedStep={selectedStep}
          onTriggerChange={(patch) => setTree((t) => ({ ...t, ...patch }))}
          onStepChange={(patch) => {
            if (!selectedId) return;
            setTree((t) => ({ ...t, steps: updateStepConfig(t.steps, selectedId, patch) }));
          }}
          onDelete={onDelete}
          onClose={() => setSelectedId(null)}
        />

        {pending && <AddStepMenu pending={{ screenPos: pending.screenPos }} onPick={handlePick} onClose={() => setPending(null)} />}
        {pendingDelete && <ConfirmDialog message={pendingDelete.message} onConfirm={confirmDelete} onCancel={() => setPendingDelete(null)} />}
      </div>
    </BuilderActionsContext.Provider>
  );
});
