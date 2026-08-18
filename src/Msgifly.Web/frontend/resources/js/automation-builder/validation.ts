import { CONDITION_SUBJECT_META, NO_SESSION_TRIGGERS, SESSION_RESTRICTED_STEPS, STEP_META } from './step-meta';
import type { AutomationTree, StepNode, ValidationIssue } from './types';

/** Port of old canvas's computeSessionWindowWarning — WhatsApp rejects a plain message/buttons
 * step as the very first thing sent to someone with no open 24h session (a brand-new Lead Ads
 * contact, or any freshly-created contact). */
export function computeSessionWindowWarning(tree: AutomationTree): string | null {
  if (!NO_SESSION_TRIGGERS.has(tree.triggerType)) return null;

  const first = tree.steps[0];
  if (!first || !SESSION_RESTRICTED_STEPS.has(first.type)) return null;

  const triggerLabel = tree.triggerType === 'FacebookLeadReceived' ? 'a new Facebook lead' : 'a new contact';
  const stepLabel = first.type === 'SendButtons' ? '"Send Buttons"' : '"Send Message"';
  return `${stepLabel} can't reach ${triggerLabel} as the first step — WhatsApp only allows a Template message to someone who hasn't messaged you yet. Use "Send Template" first instead.`;
}

/** Port of old canvas's computeValidationIssues, adapted from "no outgoing connection" (Drawflow's
 * concept) to "last element of its containing array" (this tree's equivalent), and from
 * "Condition has neither output connected" to "Condition's Yes AND No are both empty". */
export function computeValidationIssues(tree: AutomationTree): ValidationIssue[] {
  const issues: ValidationIssue[] = [];

  function walk(steps: StepNode[]) {
    steps.forEach((step, index) => {
      const label = STEP_META[step.type].label;

      // A switch (rather than an if/else-if chain) so TypeScript actually narrows step.config per
      // step.type inside each case, and so the `return` in the Condition case provably removes
      // 'Condition' from step.type's reachable set at the dead-end check below.
      switch (step.type) {
        case 'SendTemplate':
          if (!step.config.templateName.trim()) issues.push({ nodeId: step.id, message: `${label}: no template chosen yet.` });
          break;
        case 'SendMessage':
          if (!step.config.text.trim()) issues.push({ nodeId: step.id, message: `${label}: message text is empty.` });
          break;
        case 'SendButtons': {
          if (!step.config.bodyText.trim()) issues.push({ nodeId: step.id, message: `${label}: message body is empty.` });
          const hasAnyButton = step.config.buttons.some((b) => b.id.trim() && b.title.trim());
          if (!hasAnyButton) issues.push({ nodeId: step.id, message: `${label}: no buttons filled in yet.` });
          break;
        }
        case 'SendWebhook':
          if (!step.config.url.trim()) issues.push({ nodeId: step.id, message: `${label}: webhook URL is empty.` });
          break;
        case 'Condition': {
          const help = CONDITION_SUBJECT_META[step.config.subject] ?? CONDITION_SUBJECT_META.MessageContent;
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

/** A Condition step is only ever authored as the last item in its array by this builder (and the
 * old one) — the engine actually tolerates trailing siblings after a Condition, but neither UI can
 * render or author that shape. A tree loaded with that shape anyway (e.g. authored via the MCP
 * server) would have those trailing steps silently dropped on next save; this surfaces a one-time
 * non-blocking notice instead of staying silent about it. */
export function detectTrailingSiblingsAfterCondition(steps: StepNode[]): ValidationIssue[] {
  const issues: ValidationIssue[] = [];

  function walk(scope: StepNode[]) {
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
