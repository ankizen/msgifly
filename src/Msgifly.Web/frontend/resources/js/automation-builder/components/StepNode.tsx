import { Handle, Position, type NodeProps } from '@xyflow/react';
import { useBuilderActions } from '../builder-context';
import { STEP_COLOR, STEP_META } from '../step-meta';
import { summarizeStep } from '../summarize';
import { NODE_WIDTH, type StepNodeData } from '../derive-graph';

export function StepNodeCard({ id, data, selected }: NodeProps) {
  const { step } = data as unknown as StepNodeData;
  const actions = useBuilderActions();
  const meta = STEP_META[step.type];
  const color = STEP_COLOR[step.type];

  return (
    <div className="group relative" style={{ width: NODE_WIDTH }}>
      <Handle type="target" position={Position.Top} isConnectable={false} className="!w-2.5 !h-2.5 !bg-slate-400 !border-2 !border-white dark:!border-slate-800" />

      <div
        onClick={() => actions.onSelectNode(id)}
        style={selected ? { borderColor: color.accent, boxShadow: `0 0 0 3px ${color.accent}33` } : undefined}
        className={`relative flex items-start gap-2.5 rounded-xl border bg-white dark:bg-slate-800 px-3 py-2.5 cursor-pointer shadow-sm transition-shadow duration-150 hover:shadow-md ${
          selected ? '' : 'border-slate-200 dark:border-slate-600'
        }`}
      >
        <span
          className="flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center text-sm"
          style={{ background: color.tint }}
        >
          {meta.icon}
        </span>
        <div className="min-w-0 flex-1 pr-4">
          <div className="text-xs font-semibold text-slate-800 dark:text-slate-100 truncate">{meta.label}</div>
          <div className="text-[11px] text-slate-500 dark:text-slate-400 truncate mt-0.5">{summarizeStep(step)}</div>
        </div>

        <button
          type="button"
          title="Delete this step"
          onClick={(e) => {
            e.stopPropagation();
            actions.onDelete(id);
          }}
          className="absolute top-1.5 right-1.5 w-5 h-5 rounded-full flex items-center justify-center text-slate-300 hover:text-white hover:bg-red-500 opacity-0 group-hover:opacity-100 transition-opacity duration-150 leading-none"
        >
          &times;
        </button>
      </div>

      {step.type !== 'Condition' && (
        <button
          type="button"
          title="Add a step after this one"
          onClick={(e) => {
            e.stopPropagation();
            actions.onAddAfter(id, e);
          }}
          style={{ color: color.accent }}
          className="absolute -bottom-3 left-1/2 -translate-x-1/2 w-6 h-6 rounded-full bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-600 text-sm leading-none shadow transition-transform duration-150 hover:scale-110"
        >
          +
        </button>
      )}

      {step.type !== 'Condition' && (
        <Handle
          type="source"
          position={Position.Bottom}
          isConnectable={false}
          style={{ background: color.accent }}
          className="!w-2.5 !h-2.5 !border-2 !border-white dark:!border-slate-800"
        />
      )}
      {step.type === 'Condition' && (
        <>
          <Handle type="source" position={Position.Bottom} id="yes" style={{ left: '35%' }} isConnectable={false} className="!w-2.5 !h-2.5 !bg-green-600 !border-2 !border-white dark:!border-slate-800" />
          <Handle type="source" position={Position.Bottom} id="no" style={{ left: '65%' }} isConnectable={false} className="!w-2.5 !h-2.5 !bg-red-600 !border-2 !border-white dark:!border-slate-800" />
        </>
      )}
    </div>
  );
}
