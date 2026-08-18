import { Handle, Position, type NodeProps } from '@xyflow/react';
import { useBuilderActions } from '../builder-context';
import type { TriggerNodeData } from '../derive-graph';
import { TRIGGER_NODE_ID } from '../derive-graph';

const TRIGGER_LABELS: Record<string, string> = {
  InboundMessage: 'Any inbound message',
  FirstInboundMessage: 'First message from a contact',
  KeywordMatch: 'Message contains a keyword',
  NewContactCreated: 'New contact created',
  FacebookLeadReceived: 'New Facebook lead received',
  InteractiveReply: 'Button / list reply tapped',
};

export function TriggerNodeCard({ data, selected }: NodeProps) {
  const { triggerType } = data as unknown as TriggerNodeData;
  const actions = useBuilderActions();

  return (
    <div className="relative" style={{ width: 240 }}>
      <div
        onClick={() => actions.onSelectNode(TRIGGER_NODE_ID)}
        className={`rounded-md shadow-sm cursor-pointer bg-white dark:bg-slate-800 border ${
          selected ? 'border-emerald-500 ring-2 ring-emerald-500/40' : 'border-gray-200 dark:border-slate-600'
        }`}
      >
        <div className="px-2.5 py-1.5 rounded-t-md text-white text-xs font-semibold" style={{ background: '#1e293b' }}>⚡ Trigger</div>
        <div className="px-2.5 py-1.5 text-[11px] text-gray-600 dark:text-slate-300 truncate">{TRIGGER_LABELS[triggerType] ?? triggerType}</div>
      </div>

      <button
        type="button"
        title="Add the first step"
        onClick={(e) => {
          e.stopPropagation();
          actions.onAddAfter(TRIGGER_NODE_ID, e);
        }}
        className="absolute -right-3 top-1/2 -translate-y-1/2 w-6 h-6 rounded-full bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 text-emerald-600 text-sm leading-none shadow hover:bg-emerald-50 dark:hover:bg-slate-700"
      >
        +
      </button>
      <Handle type="source" position={Position.Right} isConnectable={false} className="!bg-slate-700 !border-slate-800" />
    </div>
  );
}
