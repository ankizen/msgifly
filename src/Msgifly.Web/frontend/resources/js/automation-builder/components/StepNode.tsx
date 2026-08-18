import { Handle, Position, type NodeProps } from '@xyflow/react';
import { useBuilderActions } from '../builder-context';
import { STEP_META } from '../step-meta';
import { summarizeStep } from '../summarize';
import type { StepNodeData } from '../derive-graph';

export function StepNodeCard({ id, data, selected }: NodeProps) {
  const { step } = data as unknown as StepNodeData;
  const actions = useBuilderActions();
  const meta = STEP_META[step.type];

  return (
    <div className="relative" style={{ width: 240 }}>
      <Handle type="target" position={Position.Left} isConnectable={false} className="!bg-emerald-600 !border-emerald-700" />
      <div
        onClick={() => actions.onSelectNode(id)}
        className={`rounded-md shadow-sm cursor-pointer bg-white dark:bg-slate-800 border ${
          selected ? 'border-emerald-500 ring-2 ring-emerald-500/40' : 'border-gray-200 dark:border-slate-600'
        }`}
      >
        <div className="flex items-center justify-between gap-1.5 px-2.5 py-1.5 rounded-t-md text-white text-xs font-semibold" style={{ background: '#047857' }}>
          <span className="truncate">
            {meta.icon} {meta.label}
          </span>
          <button
            type="button"
            title="Delete this step"
            onClick={(e) => {
              e.stopPropagation();
              actions.onDelete(id);
            }}
            className="flex-shrink-0 w-4 h-4 rounded text-white/90 hover:bg-white/20 leading-none"
          >
            &times;
          </button>
        </div>
        <div className="px-2.5 py-1.5 text-[11px] text-gray-600 dark:text-slate-300 truncate">{summarizeStep(step)}</div>
      </div>

      {step.type !== 'Condition' && (
        <button
          type="button"
          title="Add a step after this one"
          onClick={(e) => {
            e.stopPropagation();
            actions.onAddAfter(id, e);
          }}
          className="absolute -right-3 top-1/2 -translate-y-1/2 w-6 h-6 rounded-full bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 text-emerald-600 text-sm leading-none shadow hover:bg-emerald-50 dark:hover:bg-slate-700"
        >
          +
        </button>
      )}

      {step.type !== 'Condition' && <Handle type="source" position={Position.Right} isConnectable={false} className="!bg-emerald-600 !border-emerald-700" />}
      {step.type === 'Condition' && (
        <>
          <Handle type="source" position={Position.Right} id="yes" style={{ top: '35%' }} isConnectable={false} className="!bg-green-600 !border-green-700" />
          <Handle type="source" position={Position.Right} id="no" style={{ top: '65%' }} isConnectable={false} className="!bg-red-600 !border-red-700" />
        </>
      )}
    </div>
  );
}
