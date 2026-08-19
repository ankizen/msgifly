import type { EmailAutomationTree } from '../../types';

const FIELD_CLASS =
  'mt-1 block w-full rounded-md border-gray-300 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 shadow-sm text-xs focus:border-emerald-500 focus:ring-emerald-500';
const HINT_CLASS = 'text-[10px] leading-tight text-gray-400 dark:text-slate-500 mt-1';

/** Which trigger-type-specific field this trigger actually reads server-side
 * (EmailAutomationEngine.TriggerMatches) — SubscriberAdded/ListApplied read ListId, TagApplied
 * reads TagId, blank/0 means "any". No live list/tag picker exists yet (see step-meta.ts's
 * CONDITION_SUBJECT_META comment) — a numeric id input stands in for it this pass. */
interface Props {
  tree: EmailAutomationTree;
  onChange: (patch: Partial<EmailAutomationTree>) => void;
}

export function TriggerForm({ tree, onChange }: Props) {
  return (
    <div className="space-y-2">
      <select
        value={tree.triggerType}
        onChange={(e) => onChange({ triggerType: e.target.value as EmailAutomationTree['triggerType'] })}
        className={FIELD_CLASS}
      >
        <option value="SubscriberAdded">Subscriber added</option>
        <option value="TagApplied">Tag applied to subscriber</option>
        <option value="ListApplied">Subscriber added to list</option>
      </select>

      {(tree.triggerType === 'SubscriberAdded' || tree.triggerType === 'ListApplied') && (
        <>
          <input
            type="number"
            min={0}
            value={tree.listId ?? ''}
            onChange={(e) => onChange({ listId: e.target.value ? Number(e.target.value) : null })}
            placeholder="List id (blank = any list)"
            className={FIELD_CLASS}
          />
          <p className={HINT_CLASS}>Leave blank to fire for any list.</p>
        </>
      )}

      {tree.triggerType === 'TagApplied' && (
        <>
          <input
            type="number"
            min={0}
            value={tree.tagId ?? ''}
            onChange={(e) => onChange({ tagId: e.target.value ? Number(e.target.value) : null })}
            placeholder="Tag id (blank = any tag)"
            className={FIELD_CLASS}
          />
          <p className={HINT_CLASS}>Leave blank to fire for any tag.</p>
        </>
      )}
    </div>
  );
}
