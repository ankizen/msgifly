import { CONDITION_SUBJECT_META, STEP_META } from './step-meta';
import type { EmailAutomationTree, EmailStepNode, ValidationIssue } from './types';

/** Port of the WhatsApp canvas's computeValidationIssues, adapted from "no outgoing connection"
 * (Drawflow's concept) to "last element of its containing array" (this tree's equivalent), and from
 * "Condition has neither output connected" to "Condition's Yes AND No are both empty". Email has no
 * analogue to the WhatsApp canvas's 24h-session-window warning (that's a WhatsApp-specific
 * constraint on the first outbound message to a brand-new contact — there's no equivalent
 * restriction on sending email), so this file only covers structural/field validation. */
export function computeValidationIssues(tree: EmailAutomationTree): ValidationIssue[] {
  const issues: ValidationIssue[] = [];

  function walk(steps: EmailStepNode[]) {
    steps.forEach((step, index) => {
      const label = STEP_META[step.type].label;

      // A switch (rather than an if/else-if chain) so TypeScript actually narrows step.config per
      // step.type inside each case, and so the `return` in the Condition case provably removes
      // 'Condition' from step.type's reachable set at the dead-end check below.
      switch (step.type) {
        case 'SendEmail':
          if (!step.config.subject.trim()) issues.push({ nodeId: step.id, message: `${label}: subject line is empty.` });
          if (!step.config.bodyHtml.trim()) issues.push({ nodeId: step.id, message: `${label}: email body is empty.` });
          break;
        case 'AddTag':
        case 'RemoveTag':
          if (!step.config.tagId) issues.push({ nodeId: step.id, message: `${label}: no tag chosen yet.` });
          break;
        case 'AddToList':
        case 'RemoveFromList':
          if (!step.config.listId) issues.push({ nodeId: step.id, message: `${label}: no list chosen yet.` });
          break;
        case 'UpdateSubscriberField':
          if (!step.config.value.trim()) issues.push({ nodeId: step.id, message: `${label}: new value is empty.` });
          break;
        case 'Webhook':
          if (!step.config.url.trim()) issues.push({ nodeId: step.id, message: `${label}: webhook URL is empty.` });
          break;
        case 'Condition': {
          const help = CONDITION_SUBJECT_META[step.config.subject] ?? CONDITION_SUBJECT_META.SubscriberField;
          if (help.showOperand && !(step.config.operand ?? '').trim()) {
            issues.push({ nodeId: step.id, message: `${label}: missing ${(help.operandPlaceholder ?? 'a value').toLowerCase()}.` });
          }
          if (help.showValue && !(step.config.value ?? '').trim()) {
            issues.push({ nodeId: step.id, message: `${label}: missing a value to compare.` });
          }
          if (step.yes.length === 0 && step.no.length === 0) {
            issues.push({ nodeId: step.id, message: `${label}: doesn't lead anywhere — this branch dead-ends here. Add a "Stop" step if that's intentional.` });
          }
          walk(step.yes);
          walk(step.no);
          return; // Condition's own dead-end check above already covers "nothing after it" for both outputs
        }
        default:
          break;
      }

      if (step.type !== 'Stop' && index === steps.length - 1) {
        issues.push({ nodeId: step.id, message: `${label}: doesn't lead anywhere — this branch dead-ends here. Add a "Stop" step if that's intentional.` });
      }
    });
  }

  walk(tree.steps);
  return issues;
}

/** A Condition step is only ever authored as the last item in its array by this builder — the
 * engine actually tolerates trailing siblings after a Condition, but this UI can't render or author
 * that shape. A tree loaded with that shape anyway (e.g. authored via a future integration) would
 * have those trailing steps silently dropped on next save; this surfaces a one-time non-blocking
 * notice instead of staying silent about it. Identical logic to the WhatsApp canvas's
 * detectTrailingSiblingsAfterCondition — the tree shape it walks is generic enough to copy verbatim. */
export function detectTrailingSiblingsAfterCondition(steps: EmailStepNode[]): ValidationIssue[] {
  const issues: ValidationIssue[] = [];

  function walk(scope: EmailStepNode[]) {
    const conditionIndex = scope.findIndex((s) => s.type === 'Condition');
    if (conditionIndex !== -1 && conditionIndex < scope.length - 1) {
      const condition = scope[conditionIndex];
      const droppedCount = scope.length - conditionIndex - 1;
      issues.push({
        nodeId: condition.id,
        message: `Condition: ${droppedCount} step${droppedCount === 1 ? '' : 's'} after this one in the same sequence can't be shown here and will be dropped if you save.`,
      });
    }
    for (const step of scope) {
      if (step.type === 'Condition') {
        walk(step.yes);
        walk(step.no);
      }
    }
  }

  walk(steps);
  return issues;
}
