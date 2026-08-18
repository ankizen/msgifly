import { STEP_META } from '../step-meta';
import type { AutomationTree, LeadForm, StepNode, TemplateOption } from '../types';
import { TRIGGER_NODE_ID } from '../derive-graph';
import { TriggerForm } from './forms/TriggerForm';
import { StepForm } from './forms/StepForm';

interface Props {
  selectedId: string | null;
  tree: AutomationTree;
  selectedStep: StepNode | null;
  leadForms: LeadForm[];
  templateOptions: TemplateOption[];
  onTriggerChange: (patch: Partial<AutomationTree>) => void;
  onStepChange: (patch: Record<string, unknown>) => void;
  onDelete: (id: string) => void;
  onClose: () => void;
}

/** Hand-rolled slide-in panel — no Sheet/Dialog component exists in this repo to reach for. */
export function PropertiesPanel({ selectedId, tree, selectedStep, leadForms, templateOptions, onTriggerChange, onStepChange, onDelete, onClose }: Props) {
  if (!selectedId) return null;
  const isTrigger = selectedId === TRIGGER_NODE_ID;

  return (
    <div className="absolute top-0 right-0 bottom-0 w-80 bg-white dark:bg-slate-800 border-l border-gray-200 dark:border-slate-600 shadow-xl z-10 flex flex-col">
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200 dark:border-slate-600">
        <h3 className="text-sm font-semibold text-gray-900 dark:text-white">
          {isTrigger ? '⚡ Trigger' : selectedStep ? `${STEP_META[selectedStep.type].icon} ${STEP_META[selectedStep.type].label}` : ''}
        </h3>
        <button type="button" onClick={onClose} className="text-gray-400 hover:text-gray-600 dark:hover:text-slate-200 text-lg leading-none">
          &times;
        </button>
      </div>
      <div className="flex-1 overflow-y-auto px-4 py-3">
        {isTrigger && <TriggerForm tree={tree} leadForms={leadForms} onChange={onTriggerChange} />}
        {!isTrigger && selectedStep && <StepForm step={selectedStep} templateOptions={templateOptions} onChange={onStepChange} />}
      </div>
      {!isTrigger && selectedStep && (
        <div className="px-4 py-3 border-t border-gray-200 dark:border-slate-600">
          <button
            type="button"
            onClick={() => onDelete(selectedId)}
            className="w-full px-3 py-1.5 rounded-md border border-red-300 dark:border-red-700 text-red-600 dark:text-red-400 text-xs font-medium hover:bg-red-50 dark:hover:bg-red-900/20"
          >
            Delete this step
          </button>
        </div>
      )}
    </div>
  );
}
