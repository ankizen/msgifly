import type { AutomationTree, LeadForm } from '../../types';

const FIELD_CLASS =
  'mt-1 block w-full rounded-md border-gray-300 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 shadow-sm text-xs focus:border-emerald-500 focus:ring-emerald-500';

interface Props {
  tree: AutomationTree;
  leadForms: LeadForm[];
  onChange: (patch: Partial<AutomationTree>) => void;
}

// Which trigger-type-specific field group each type actually reads server-side
// (AutomationEngine.TriggerMatches) — showing every field for every trigger type regardless of
// selection is confusing clutter on the very first node every automation has.
export function TriggerForm({ tree, leadForms, onChange }: Props) {
  return (
    <div className="space-y-2">
      <select value={tree.triggerType} onChange={(e) => onChange({ triggerType: e.target.value })} className={FIELD_CLASS}>
        <option value="InboundMessage">Any inbound message</option>
        <option value="FirstInboundMessage">First message from a contact</option>
        <option value="KeywordMatch">Message contains a keyword</option>
        <option value="NewContactCreated">New contact created (any source)</option>
        <option value="FacebookLeadReceived">New Facebook lead received</option>
        <option value="InteractiveReply">Button / list reply tapped</option>
      </select>

      {tree.triggerType === 'KeywordMatch' && (
        <>
          <input
            value={tree.keywords}
            onChange={(e) => onChange({ keywords: e.target.value })}
            placeholder="Keywords, comma-separated"
            className={FIELD_CLASS}
          />
          <select value={tree.matchType} onChange={(e) => onChange({ matchType: e.target.value as AutomationTree['matchType'] })} className={FIELD_CLASS}>
            <option value="contains">Contains</option>
            <option value="exact">Exact match</option>
            <option value="word">Whole word</option>
          </select>
          <select value={tree.caseSensitive ? 'true' : 'false'} onChange={(e) => onChange({ caseSensitive: e.target.value === 'true' })} className={FIELD_CLASS}>
            <option value="false">Case-insensitive</option>
            <option value="true">Case-sensitive</option>
          </select>
        </>
      )}

      {tree.triggerType === 'InteractiveReply' && (
        <input
          value={tree.replyIds}
          onChange={(e) => onChange({ replyIds: e.target.value })}
          placeholder="Button/row ids, comma-separated"
          className={FIELD_CLASS}
        />
      )}

      {tree.triggerType === 'FacebookLeadReceived' && (
        <select value={tree.leadFormId} onChange={(e) => onChange({ leadFormId: e.target.value })} className={FIELD_CLASS}>
          <option value="">Any Facebook Lead Ads form</option>
          {leadForms.map((f) => (
            <option key={f.id} value={f.id}>
              {f.name}
            </option>
          ))}
        </select>
      )}
    </div>
  );
}
