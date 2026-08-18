import { Handle, Position, type NodeProps } from '@xyflow/react';
import { useBuilderActions } from '../builder-context';
import type { EmptySlotNodeData } from '../derive-graph';

/** The only genuinely-empty attachment point this builder needs a placeholder card for — a
 * Condition's Yes or No branch with zero steps. Every other "add a step" interaction happens via
 * the "+" button rendered on an existing node (Trigger or any step) instead, since there's always
 * a real anchor to hang it off of there. */
export function EmptySlotNodeCard({ data }: NodeProps) {
  const { scope, label } = data as unknown as EmptySlotNodeData;
  const actions = useBuilderActions();
  const color = label === 'Yes' ? 'border-green-300 text-green-700 hover:bg-green-50 hover:border-green-400' : 'border-red-300 text-red-700 hover:bg-red-50 hover:border-red-400';

  return (
    <div style={{ width: 170 }}>
      <Handle type="target" position={Position.Left} isConnectable={false} className="!bg-transparent !border-transparent" />
      <button
        type="button"
        onClick={(e) => actions.onAddAtSlot(scope, e)}
        className={`w-full rounded-xl border-2 border-dashed px-2 py-3 text-xs font-medium bg-white/60 dark:bg-slate-800/60 dark:border-slate-600 transition-all duration-150 hover:scale-[1.02] hover:shadow-sm ${color}`}
      >
        + Add {label} step
      </button>
    </div>
  );
}
