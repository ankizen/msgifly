import type { EmailStepNode } from './types';

/** One-line "what does this card actually do" summary rendered on each block card — the direct
 * equivalent of the FluentCRM funnel editor's getBlockDescription(block) (see Edit.js): a
 * send_custom_email block shows its actual subject line, fluentcrm_wait_times shows "Wait 2
 * hours", add_contact_to_list shows the resolved list name once available. This builder has no
 * live tag/list name lookup wired up yet (see step-meta.ts's CONDITION_SUBJECT_META comment), so
 * AddTag/RemoveTag/AddToList/RemoveFromList fall back to showing the numeric id — an explicitly
 * sanctioned fallback, not a placeholder to remove. */
export function summarizeStep(step: EmailStepNode): string {
  switch (step.type) {
    case 'SendEmail':
      return step.config.subject.trim() || 'Set email subject & body';
    case 'Wait':
      return `Wait ${step.config.amount} ${step.config.unit}`;
    case 'Condition': {
      const c = step.config;
      if (c.subject === 'HasTag') return c.operand ? `Subscriber has tag #${c.operand}` : 'Set tag to check';
      if (c.subject === 'HasList') return c.operand ? `Subscriber is on list #${c.operand}` : 'Set list to check';
      return c.operand ? `Field ${c.operand} = ${c.value || '…'}` : 'Set field to check';
    }
    case 'AddTag':
      return step.config.tagId ? `Add tag #${step.config.tagId}` : 'Set tag to add';
    case 'RemoveTag':
      return step.config.tagId ? `Remove tag #${step.config.tagId}` : 'Set tag to remove';
    case 'AddToList':
      return step.config.listId ? `Add to list #${step.config.listId}` : 'Set list to add to';
    case 'RemoveFromList':
      return step.config.listId ? `Remove from list #${step.config.listId}` : 'Set list to remove from';
    case 'UpdateSubscriberField':
      return step.config.value.trim() ? `Update ${step.config.field} = ${step.config.value}` : `Update ${step.config.field}`;
    case 'Webhook':
      return step.config.url.trim() || 'Set webhook URL';
    case 'Stop':
      return 'Ends this branch';
    default:
      return '';
  }
}
