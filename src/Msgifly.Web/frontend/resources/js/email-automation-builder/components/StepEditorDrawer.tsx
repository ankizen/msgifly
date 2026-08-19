import { STEP_COLOR, STEP_META, TRIGGER_COLOR } from '../step-meta';
import type { EmailAutomationTree, EmailStepNode } from '../types';
import { TriggerForm } from './forms/TriggerForm';
import { StepForm } from './forms/StepForm';
import { Drawer } from './Drawer';

interface Props {
  /** null: drawer closed. 'trigger': editing the trigger card. otherwise: editing this step id. */
  target: 'trigger' | EmailStepNode | null;
  tree: EmailAutomationTree;
  onTriggerChange: (patch: Partial<EmailAutomationTree>) => void;
  onStepChange: (patch: Record<string, unknown>) => void;
  onDelete: (id: string) => void;
  onClose: () => void;
}

/** The right-side settings drawer — the FluentCRM funnel editor opens the exact same kind of panel
 * (titled "Edit {block title}") when you click directly on a block; the ⋮ menu is reserved for
 * Delete/Clone instead (see StepRow.tsx). Every edit here commits straight into the canonical tree
 * on each keystroke (same live-binding this builder already used before the rewrite) — there's no
 * separate "working copy + Save button" step to replicate from FluentCRM's Vue implementation. */
export function StepEditorDrawer({ target, tree, onTriggerChange, onStepChange, onDelete, onClose }: Props) {
  const isTrigger = target === 'trigger';
  const step = isTrigger ? null : target;
  const color = isTrigger ? TRIGGER_COLOR : step ? STEP_COLOR[step.type] : null;
  const title = isTrigger ? 'Edit Trigger' : step ? `Edit ${STEP_META[step.type].label}` : '';

  return (
    <Drawer
      open={target != null}
      onClose={onClose}
      title={title}
      footer={
        step && (
          <button
            type="button"
            onClick={() => onDelete(step.id)}
            className="w-full px-3 py-1.5 rounded-md border border-red-300 dark:border-red-700 text-red-600 dark:text-red-400 text-xs font-medium hover:bg-red-50 dark:hover:bg-red-900/20"
          >
            Delete this step
          </button>
        )
      }
    >
      <div className="flex items-center gap-2.5 mb-4">
        {color && (
          <span className="flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center text-sm" style={{ background: isTrigger ? color.accent : color.tint }}>
            {isTrigger ? <span className="text-white">⚡</span> : step ? STEP_META[step.type].icon : null}
          </span>
        )}
        <div className="text-xs text-gray-500 dark:text-slate-400">{isTrigger ? 'When this automation starts' : step ? STEP_META[step.type].label : ''}</div>
      </div>

      {isTrigger && <TriggerForm tree={tree} onChange={onTriggerChange} />}
      {step && <StepForm step={step} onChange={onStepChange} />}
    </Drawer>
  );
}
