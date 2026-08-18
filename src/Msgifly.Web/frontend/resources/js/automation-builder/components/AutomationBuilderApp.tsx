import { forwardRef, useCallback, useEffect, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { Background, Controls, MiniMap, ReactFlow, type Edge, type NodeMouseHandler, type OnNodeDrag, type ReactFlowInstance } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { BuilderActionsContext, type BuilderActions } from '../builder-context';
import { deriveGraph, heightFor, TRIGGER_NODE_ID, type BuilderNode } from '../derive-graph';
import { computeMissingPositions, tidyLayout, type Point } from '../layout';
import type { InsertScope } from '../tree';
import { countDescendants, deleteStep, findNode, insertAfterNode, insertAtScopeStart, newStep, treeFromWire, treeToWire, updateStepConfig } from '../tree';
import { computeSessionWindowWarning, computeValidationIssues, detectTrailingSiblingsAfterCondition } from '../validation';
import type { AutomationBuilderHandle, AutomationTree, ExportResult, InitialProps, OnChangeState, StepType } from '../types';
import { AddStepMenu } from './AddStepMenu';
import { PropertiesPanel } from './PropertiesPanel';
import { Toolbar } from './Toolbar';
import { StepNodeCard } from './StepNode';
import { TriggerNodeCard } from './TriggerNode';
import { EmptySlotNodeCard } from './EmptySlotNode';

const NODE_TYPES = { trigger: TriggerNodeCard, step: StepNodeCard, emptySlot: EmptySlotNodeCard };
const LAYOUT_NODE_WIDTH = 240;

type PendingTarget = { kind: 'root' } | { kind: 'after'; anchorId: string } | { kind: 'slot'; scope: InsertScope };
interface PendingState {
  screenPos: { x: number; y: number };
  target: PendingTarget;
}

interface Props {
  initial: InitialProps;
  onChange: (state: OnChangeState) => void;
}

function buildInitialTree(initial: InitialProps): AutomationTree {
  return {
    triggerType: initial.triggerType || 'InboundMessage',
    keywords: initial.triggerFields?.keywords ?? '',
    matchType: (initial.triggerFields?.matchtype as AutomationTree['matchType']) ?? 'contains',
    caseSensitive: initial.triggerFields?.casesensitive === 'true',
    replyIds: initial.triggerFields?.replyids ?? '',
    leadFormId: initial.triggerFields?.leadformid ?? '',
    steps: treeFromWire(initial.steps ?? []),
  };
}

export const AutomationBuilderApp = forwardRef<AutomationBuilderHandle, Props>(function AutomationBuilderApp({ initial, onChange }, ref) {
  const [tree, setTree] = useState<AutomationTree>(() => buildInitialTree(initial));
  const [positions, setPositions] = useState<Map<string, Point>>(new Map());
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [pending, setPending] = useState<PendingState | null>(null);
  const rfInstance = useRef<ReactFlowInstance<BuilderNode, Edge> | null>(null);
  const hasRenderedOnce = useRef(false);

  const leadForms = useMemo(() => initial.leadForms ?? [], [initial]);
  const templateOptions = useMemo(() => initial.templateOptions ?? [], [initial]);

  // Detected once, off the tree exactly as loaded — this shape (a Condition with trailing
  // siblings in its own array) can only ever arrive from outside this builder (e.g. the MCP
  // server), never be produced by it, so there's no need to recheck after every edit.
  const trailingSiblingIssues = useMemo(() => detectTrailingSiblingsAfterCondition(treeFromWire(initial.steps ?? [])), [initial]);

  const { nodes: baseNodes, edges } = useMemo(() => deriveGraph(tree), [tree]);

  // Positions are local-only state, deliberately outside the canonical tree (the wire contract has
  // no position field at all — every load re-lays-out from scratch, matching old canvas exactly).
  // Runs before paint (not useEffect) so a newly-added node's fallback position is never visible.
  useLayoutEffect(() => {
    const layoutNodes = baseNodes.map((n) => ({ id: n.id, width: LAYOUT_NODE_WIDTH, height: heightFor(n) }));
    const missing = computeMissingPositions(layoutNodes, edges, positions);
    if (missing.size > 0) {
      setPositions((prev) => {
        const next = new Map(prev);
        missing.forEach((p, id) => next.set(id, p));
        return next;
      });
    }
    // positions intentionally excluded — this effect only ever ADDS entries for ids missing from
    // it, so depending on it would either no-op or self-trigger on every drag; re-running only
    // when the node/edge SET actually changes is what's wanted here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [baseNodes, edges]);

  const nodes: BuilderNode[] = useMemo(
    () => baseNodes.map((n) => ({ ...n, position: positions.get(n.id) ?? { x: -9999, y: -9999 } })),
    [baseNodes, positions]
  );

  const selectedStep = useMemo(() => (selectedId && selectedId !== TRIGGER_NODE_ID ? findNode(tree.steps, selectedId) : null), [tree.steps, selectedId]);

  useEffect(() => {
    onChange({
      sessionWindowWarning: computeSessionWindowWarning(tree),
      validationIssues: [...computeValidationIssues(tree), ...trailingSiblingIssues],
      isInitialRender: !hasRenderedOnce.current,
    });
    hasRenderedOnce.current = true;
    // onChange/trailingSiblingIssues excluded: onChange is the caller's stable callback, and
    // trailingSiblingIssues never changes after mount (see the useMemo above) — only `tree` edits
    // should trigger a recompute.
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
      if (!window.confirm(message)) return;
      const result = deleteStep(tree.steps, id);
      setTree((t) => ({ ...t, steps: result.steps }));
      setPositions((prev) => {
        const next = new Map(prev);
        next.delete(id);
        return next;
      });
      if (selectedId === id) setSelectedId(null);
    },
    [tree.steps, selectedId]
  );

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

  function handlePick(type: StepType) {
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
  }

  useImperativeHandle(
    ref,
    (): AutomationBuilderHandle => ({
      exportForSubmit(): ExportResult {
        return {
          triggerType: tree.triggerType,
          keywords: tree.keywords,
          matchType: tree.matchType,
          caseSensitive: tree.caseSensitive,
          replyIds: tree.replyIds,
          leadFormId: tree.leadFormId,
          stepsJson: JSON.stringify(treeToWire(tree.steps)),
        };
      },
      focusNode(nodeId: string) {
        const pos = positions.get(nodeId);
        if (pos && rfInstance.current) {
          rfInstance.current.setCenter(pos.x + LAYOUT_NODE_WIDTH / 2, pos.y + 60, { zoom: 1, duration: 400 });
        }
        setSelectedId(nodeId);
      },
      tidyUp() {
        const layoutNodes = baseNodes.map((n) => ({ id: n.id, width: LAYOUT_NODE_WIDTH, height: heightFor(n) }));
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
          <Background />
          <Controls showInteractive={false} />
          <MiniMap pannable zoomable className="!bg-white dark:!bg-slate-800" />
        </ReactFlow>

        <Toolbar
          onTidyUp={() => {
            const layoutNodes = baseNodes.map((n) => ({ id: n.id, width: LAYOUT_NODE_WIDTH, height: heightFor(n) }));
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
          leadForms={leadForms}
          templateOptions={templateOptions}
          onTriggerChange={(patch) => setTree((t) => ({ ...t, ...patch }))}
          onStepChange={(patch) => {
            if (!selectedId) return;
            setTree((t) => ({ ...t, steps: updateStepConfig(t.steps, selectedId, patch) }));
          }}
          onDelete={onDelete}
          onClose={() => setSelectedId(null)}
        />

        {pending && <AddStepMenu pending={{ screenPos: pending.screenPos }} onPick={handlePick} onClose={() => setPending(null)} />}
      </div>
    </BuilderActionsContext.Provider>
  );
});
