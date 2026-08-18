import type { StepNode } from './types';

/** One-line "what does this card actually do" summary shown under the label — mirrors wacrm's
 * summarizeNode() idea, adapted to this app's 8 step types. */
export function summarizeStep(step: StepNode): string {
  switch (step.type) {
    case 'SendMessage':
      return step.config.text.trim() || '(empty message)';
    case 'SendTemplate':
      return step.config.templateName || '(no template chosen)';
    case 'SendButtons':
      return step.config.bodyText.trim() || '(empty message)';
    case 'Wait':
      return `${step.config.amount} ${step.config.unit}`;
    case 'Condition':
      return step.config.subject;
    case 'UpdateContactField':
      return `${step.config.field} = ${step.config.value || '…'}`;
    case 'SendWebhook':
      return step.config.url.trim() || '(no URL)';
    case 'Stop':
      return 'Ends this branch';
    default:
      return '';
  }
}
