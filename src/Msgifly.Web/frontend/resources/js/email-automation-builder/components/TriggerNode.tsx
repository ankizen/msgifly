import { Handle, Position, type NodeProps } from '@xyflow/react';
import { useBuilderActions } from '../builder-context';
import { TRIGGER_COLOR } from '../step-meta';
import { NODE_WIDTH, TRIGGER_NODE_ID, type TriggerNodeData } from '../derive-graph';

// EmailAutomationTriggerType's 3 members (Models/Enums/EmailAutomationEnums.cs).
const TRIGGER_LABELS: Record<string, string> = {
  SubscriberAdded: 'Subscriber added',
  TagApplied: 'Tag applied to subscriber',
  ListApplied: 'Subscriber added to list',
};

export function TriggerNodeCard({ data, selected }: NodeProps) {
  const { triggerType } = data as unknown as TriggerNodeData;
  const actions = useBuilderActions();

  return (
    <div className="relative" style={{ width: NODE_WIDTH }}>
      <div
        onClick={() => actions.onSelectNode(TRIGGER_NODE_ID)}
        style={{ background: TRIGGER_COLOR.accent, boxShadow: selected ? `0 0 0 3px ${TRIGGER_COLOR.accent}55` : undefined }}
        className="flex items-center gap-2.5 rounded-full pl-2 pr-4 py-2 cursor-pointer shadow-sm transition-shadow duration-150 hover:shadow-md"
      >
        <span className="flex-shrink-0 w-8 h-8 rounded-full bg-white/15 flex items-center justify-center text-sm">⚡</span>
        <div className="min-w-0">
          <div className="text-[10px] font-semibold uppercase tracking-wider text-white/70">Trigger</div>
          <div className="text-xs font-medium text-white truncate">{TRIGGER_LABELS[triggerType] ?? triggerType}</div>
        </div>
      </div>

      <button
        type="button"
        title="Add the first step"
        onClick={(e) => {
          e.stopPropagation();
          actions.onAddAfter(TRIGGER_NODE_ID, e);
        }}
        style={{ background: TRIGGER_COLOR.accent }}
        className="nodrag absolute -right-3 top-1/2 -translate-y-1/2 w-6 h-6 rounded-full flex items-center justify-center text-white text-sm leading-none shadow-md ring-2 ring-white dark:ring-slate-900 transition-transform duration-150 hover:scale-110"
      >
        +
      </button>
      <Handle type="source" position={Position.Right} isConnectable={false} style={{ background: TRIGGER_COLOR.accent }} className="!w-2.5 !h-2.5 !border-2 !border-white dark:!border-slate-800" />
    </div>
  );
}
