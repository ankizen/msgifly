import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState } from 'react';
import type { AddTarget } from '../tree';
import { cloneStep, countDescendants, deleteStep, findNode, insertAfterNode, insertAtScopeStart, moveStep, newStep, treeFromWire, treeToWire, updateStepConfig } from '../tree';
import { computeValidationIssues } from '../validation';
import type { EmailAutomationBuilderHandle, EmailAutomationTree, EmailStepType, ExportResult, InitialProps, OnChangeState } from '../types';
import { AddStepDrawer } from './AddStepDrawer';
import { ConfirmDialog } from './ConfirmDialog';
import { StepEditorDrawer } from './StepEditorDrawer';
import { StepList } from './StepList';
import { TriggerCard } from './TriggerCard';

const TRIGGER_ID = 'trigger';

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

/** Root of the rebuilt email automation builder — a plain top-to-bottom scrolling list of step
 * cards (StepList) under a trigger card, matching the FluentCRM funnel editor's actual layout
 * (fluentcrm_blocks_container > fluentcrm_blocks_wrapper > fluentcrm_blocks in its Edit.js) instead
 * of the rejected React-Flow node-graph canvas this replaces. Editing and adding steps both happen
 * in right-side slide-in drawers (StepEditorDrawer / AddStepDrawer) rather than a canvas-docked
 * panel or floating popover. Same exported handle contract as before
 * (exportForSubmit/focusNode/destroy — zoomIn/zoomOut/zoomReset/tidyUp dropped, see types.ts) so
 * Save.cshtml's Alpine glue needs no changes. */
export const EmailAutomationBuilderApp = forwardRef<EmailAutomationBuilderHandle, Props>(function EmailAutomationBuilderApp({ initial, onChange }, ref) {
  const [tree, setTree] = useState<EmailAutomationTree>(() => buildInitialTree(initial));
  const [editTargetId, setEditTargetId] = useState<string | null>(null);
  const [addTarget, setAddTarget] = useState<AddTarget | null>(null);
  const [pendingDelete, setPendingDelete] = useState<{ id: string; message: string } | null>(null);
  const hasRenderedOnce = useRef(false);
  const containerRef = useRef<HTMLDivElement>(null);

  const editStep = useMemo(() => (editTargetId && editTargetId !== TRIGGER_ID ? findNode(tree.steps, editTargetId) : null), [tree.steps, editTargetId]);

  useEffect(() => {
    onChange({
      validationIssues: computeValidationIssues(tree),
      isInitialRender: !hasRenderedOnce.current,
    });
    hasRenderedOnce.current = true;
    // onChange excluded: it's the caller's stable callback — only `tree` edits should recompute.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tree]);

  function requestDelete(id: string) {
    const node = findNode(tree.steps, id);
    if (!node) return;
    const count = countDescendants(node);
    const message =
      node.type === 'Condition' && count > 0
        ? `Delete this step and the ${count} step${count === 1 ? '' : 's'} in its Yes/No branches below it? This can't be undone.`
        : "Delete this step? This can't be undone.";
    setPendingDelete({ id, message });
  }

  function confirmDelete() {
    if (!pendingDelete) return;
    const { id } = pendingDelete;
    setTree((t) => ({ ...t, steps: deleteStep(t.steps, id).steps }));
    if (editTargetId === id) setEditTargetId(null);
    setPendingDelete(null);
  }

  function handlePickType(type: EmailStepType) {
    if (!addTarget) return;
    const node = newStep(type);
    setTree((t) => ({
      ...t,
      steps: addTarget.kind === 'after' ? insertAfterNode(t.steps, addTarget.anchorId, node) : insertAtScopeStart(t.steps, addTarget.scope, node),
    }));
    setAddTarget(null);
    setEditTargetId(node.id); // FluentCRM's own BlockChoice flow: pick a type, its settings drawer opens immediately.
  }

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
        if (nodeId === TRIGGER_ID) {
          setEditTargetId(TRIGGER_ID);
          return;
        }
        setEditTargetId(nodeId);
        const el = containerRef.current?.querySelector<HTMLElement>(`[data-step-id="${CSS.escape(nodeId)}"]`);
        el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      },
      destroy() {
        // Nothing to tear down explicitly — no external instance (no ReactFlow, no dagre) holds a
        // reference outside React's own tree; root.unmount() in index.tsx handles the rest.
      },
    }),
    [tree]
  );

  return (
    <div ref={containerRef} className="p-3 sm:p-4 space-y-2 max-w-2xl mx-auto">
      <TriggerCard tree={tree} onEdit={() => setEditTargetId(TRIGGER_ID)} />

      <StepList
        steps={tree.steps}
        scope={{ kind: 'root' }}
        onEdit={setEditTargetId}
        onOpenAdd={setAddTarget}
        onDelete={requestDelete}
        onClone={(id) => setTree((t) => ({ ...t, steps: cloneStep(t.steps, id) }))}
        onMove={(id, direction) => setTree((t) => ({ ...t, steps: moveStep(t.steps, id, direction) }))}
      />

      <StepEditorDrawer
        target={editTargetId === TRIGGER_ID ? 'trigger' : editStep}
        tree={tree}
        onTriggerChange={(patch) => setTree((t) => ({ ...t, ...patch }))}
        onStepChange={(patch) => {
          if (!editTargetId || editTargetId === TRIGGER_ID) return;
          setTree((t) => ({ ...t, steps: updateStepConfig(t.steps, editTargetId, patch) }));
        }}
        onDelete={requestDelete}
        onClose={() => setEditTargetId(null)}
      />

      <AddStepDrawer open={addTarget != null} onPick={handlePickType} onClose={() => setAddTarget(null)} />

      {pendingDelete && <ConfirmDialog message={pendingDelete.message} onConfirm={confirmDelete} onCancel={() => setPendingDelete(null)} />}
    </div>
  );
});
