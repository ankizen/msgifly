import { STEP_COLOR, STEP_META, TRIGGER_COLOR } from '../step-meta';
import type { EmailAutomationTree, EmailStepNode } from '../types';
import { TRIGGER_NODE_ID } from '../derive-graph';
import { TriggerForm } from './forms/TriggerForm';
import { StepForm } from './forms/StepForm';

interface Props {
  selectedId: string | null;
  tree: EmailAutomationTree;
  selectedStep: EmailStepNode | null;
  onTriggerChange: (patch: Partial<EmailAutomationTree>) => void;
  onStepChange: (patch: Record<string, unknown>) => void;
  onDelete: (id: string) => void;
  onClose: () => void;
}

/** Hand-rolled slide-in panel — no Sheet/Dialog component exists in this repo to reach for. Stays
 * mounted (translated off-screen) rather than unmounting on close, so a plain CSS transition gets
 * a real open/close slide instead of an instant pop. Identical structure to the WhatsApp canvas's
 * PropertiesPanel, minus the leadForms/templateOptions lookup props Email has no equivalent for. */
export function PropertiesPanel({ selectedId, tree, selectedStep, onTriggerChange, onStepChange, onDelete, onClose }: Props) {
  const open = selectedId != null;
  const isTrigger = selectedId === TRIGGER_NODE_ID;
  const color = isTrigger ? TRIGGER_COLOR : selectedStep ? STEP_COLOR[selectedStep.type] : null;

  return (
    <div
      className={`absolute top-0 right-0 bottom-0 w-80 bg-white dark:bg-slate-800 border-l border-slate-200 dark:border-slate-600 shadow-xl z-10 flex flex-col transition-transform duration-200 ease-out ${
        open ? 'translate-x-0' : 'translate-x-full pointer-events-none'
      }`}
    >
      {open && (
        <>
          <div className="flex items-center gap-2.5 px-4 py-3 border-b border-slate-200 dark:border-slate-600">
            {color && (
              <span
                className="flex-shrink-0 w-7 h-7 rounded-full flex items-center justify-center text-sm"
                style={{ background: isTrigger ? color.accent : color.tint }}
              >
                {isTrigger ? <span className="text-white">⚡</span> : selectedStep ? STEP_META[selectedStep.type].icon : null}
              </span>
            )}
            <h3 className="text-sm font-semibold text-slate-900 dark:text-white flex-1 truncate">
              {isTrigger ? 'Trigger' : selectedStep ? STEP_META[selectedStep.type].label : ''}
            </h3>
            <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 text-lg leading-none">
              &times;
            </button>
          </div>
          <div className="flex-1 overflow-y-auto px-4 py-3">
            {isTrigger && <TriggerForm tree={tree} onChange={onTriggerChange} />}
            {!isTrigger && selectedStep && <StepForm step={selectedStep} onChange={onStepChange} />}
          </div>
          {!isTrigger && selectedStep && selectedId && (
            <div className="px-4 py-3 border-t border-slate-200 dark:border-slate-600">
              <button
                type="button"
                onClick={() => onDelete(selectedId)}
                className="w-full px-3 py-1.5 rounded-md border border-red-300 dark:border-red-700 text-red-600 dark:text-red-400 text-xs font-medium hover:bg-red-50 dark:hover:bg-red-900/20"
              >
                Delete this step
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
