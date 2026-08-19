import type { EmailStepNode } from './types';

/** One-line "what does this card actually do" summary shown under the label — mirrors the WhatsApp
 * canvas's summarizeStep(), adapted to this builder's 10 step types. */
export function summarizeStep(step: EmailStepNode): string {
  switch (step.type) {
    case 'SendEmail':
      return step.config.subject.trim() || '(no subject)';
    case 'Wait':
      return `${step.config.amount} ${step.config.unit}`;
    case 'Condition':
      return step.config.subject;
    case 'AddTag':
      return step.config.tagId ? `Tag #${step.config.tagId}` : '(no tag chosen)';
    case 'RemoveTag':
      return step.config.tagId ? `Tag #${step.config.tagId}` : '(no tag chosen)';
    case 'AddToList':
      return step.config.listId ? `List #${step.config.listId}` : '(no list chosen)';
    case 'RemoveFromList':
      return step.config.listId ? `List #${step.config.listId}` : '(no list chosen)';
    case 'UpdateSubscriberField':
      return `${step.config.field} = ${step.config.value || '…'}`;
    case 'Webhook':
      return step.config.url.trim() || '(no URL)';
    case 'Stop':
      return 'Ends this branch';
    default:
      return '';
  }
}
