import { TRIGGER_COLOR } from '../step-meta';
import type { EmailAutomationTree } from '../types';

// EmailAutomationTriggerType's 3 members (Models/Enums/EmailAutomationEnums.cs).
const TRIGGER_LABELS: Record<string, string> = {
  SubscriberAdded: 'Subscriber added',
  TagApplied: 'Tag applied to subscriber',
  ListApplied: 'Subscriber added to list',
};

function describeTrigger(tree: EmailAutomationTree): string {
  if (tree.triggerType === 'TagApplied') return tree.tagId ? `When tag #${tree.tagId} is applied` : 'Fires for any tag';
  if (tree.triggerType === 'ListApplied') return tree.listId ? `When added to list #${tree.listId}` : 'Fires for any list';
  return tree.listId ? `When added to list #${tree.listId}` : 'Fires for any list';
}

interface Props {
  tree: EmailAutomationTree;
  onEdit: () => void;
}

/** The trigger card at the top of the page — FluentCRM's own funnel editor shows the trigger as its
 * own block/card above the step list (not a canvas node), clickable to open a "change trigger"
 * panel (TriggerChanger in Edit.js). This is the block-list equivalent: clicking it opens the same
 * StepEditorDrawer used for every step, just with the trigger's own form inside. */
export function TriggerCard({ tree, onEdit }: Props) {
  return (
    <button
      type="button"
      onClick={onEdit}
      className="w-full flex items-center gap-3 rounded-xl px-4 py-3 text-left shadow-sm hover:shadow-md transition-shadow duration-150"
      style={{ background: TRIGGER_COLOR.accent }}
    >
      <span className="flex-shrink-0 w-9 h-9 rounded-full bg-white/15 flex items-center justify-center text-base">⚡</span>
      <div className="min-w-0">
        <div className="text-[10px] font-semibold uppercase tracking-wider text-white/70">Trigger</div>
        <div className="text-sm font-medium text-white truncate">{TRIGGER_LABELS[tree.triggerType] ?? tree.triggerType}</div>
        <div className="text-xs text-white/70 truncate">{describeTrigger(tree)}</div>
      </div>
    </button>
  );
}
