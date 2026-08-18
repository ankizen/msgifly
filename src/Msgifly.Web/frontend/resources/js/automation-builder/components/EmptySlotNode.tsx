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
  const color = label === 'Yes' ? 'border-green-300 text-green-700 hover:bg-green-50' : 'border-red-300 text-red-700 hover:bg-red-50';

  return (
    <div style={{ width: 160 }}>
      <Handle type="target" position={Position.Left} isConnectable={false} className="!bg-transparent !border-transparent" />
      <button
        type="button"
        onClick={(e) => actions.onAddAtSlot(scope, e)}
        className={`w-full rounded-md border-2 border-dashed px-2 py-3 text-xs font-medium bg-white dark:bg-slate-800 dark:border-slate-600 ${color}`}
      >
        + Add {label} step
      </button>
    </div>
  );
}
