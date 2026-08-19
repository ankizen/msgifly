import type { ConditionSubject, EmailStepType } from './types';

export interface StepMetaEntry {
  label: string;
  icon: string;
  category: 'Messaging' | 'Logic' | 'Actions';
  /** One-line blurb shown on each row of the Add-step drawer (FluentCRM's own block picker shows
   * the same icon+title+description shape per row). */
  description: string;
}

export const STEP_META: Record<EmailStepType, StepMetaEntry> = {
  SendEmail: { label: 'Send Email', icon: '✉️', category: 'Messaging', description: 'Send a one-off email to the subscriber.' },
  Wait: { label: 'Wait', icon: '⏱', category: 'Logic', description: 'Pause this branch for a set amount of time.' },
  Condition: { label: 'Condition (if/else)', icon: '🔀', category: 'Logic', description: 'Branch into Yes/No paths based on a subscriber check.' },
  AddTag: { label: 'Add Tag', icon: '🏷️', category: 'Actions', description: 'Apply a tag to the subscriber.' },
  RemoveTag: { label: 'Remove Tag', icon: '🚫', category: 'Actions', description: 'Remove a tag from the subscriber.' },
  AddToList: { label: 'Add to List', icon: '📋', category: 'Actions', description: 'Add the subscriber to a list.' },
  RemoveFromList: { label: 'Remove from List', icon: '📤', category: 'Actions', description: 'Remove the subscriber from a list.' },
  UpdateSubscriberField: { label: 'Update Subscriber Field', icon: '✏️', category: 'Actions', description: 'Set a field on the subscriber’s profile.' },
  Webhook: { label: 'Call Webhook', icon: '🌐', category: 'Actions', description: 'Send subscriber data to an external URL.' },
  Stop: { label: 'Stop', icon: '⏹', category: 'Actions', description: 'End this branch here.' },
};

export interface StepColor {
  /** Solid accent — icon badge background, left rail, ring on selection, edge/handle tint. */
  accent: string;
  /** Translucent tint for the icon badge's own background so the accent icon sits on a soft chip. */
  tint: string;
}

// Same rgba-tint-of-a-hex trick as the WhatsApp canvas's step-meta.ts (see there for why a flat
// pastel hex tuned for a white canvas breaks in dark mode).
function tintOf(accentHex: string, alpha = 0.16): string {
  const n = parseInt(accentHex.slice(1), 16);
  const r = (n >> 16) & 255;
  const g = (n >> 8) & 255;
  const b = n & 255;
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

// One distinct hue per step type — applied via inline style, not Tailwind color classes, for the
// same JIT-scanner reason the WhatsApp canvas avoids them (a dynamic "bg-{color}-500" never
// generates).
const STEP_ACCENTS: Record<EmailStepType, string> = {
  SendEmail: '#2563eb',
  Wait: '#d97706',
  Condition: '#ea580c',
  AddTag: '#0d9488',
  RemoveTag: '#be123c',
  AddToList: '#7c3aed',
  RemoveFromList: '#a16207',
  UpdateSubscriberField: '#db2777',
  Webhook: '#0891b2',
  Stop: '#dc2626',
};

export const STEP_COLOR: Record<EmailStepType, StepColor> = Object.fromEntries(
  Object.entries(STEP_ACCENTS).map(([type, accent]) => [type, { accent, tint: tintOf(accent) }])
) as Record<EmailStepType, StepColor>;

// The flow's origin gets its own distinct treatment (indigo) — never reused by a step color above.
export const TRIGGER_COLOR: StepColor = { accent: '#4338ca', tint: tintOf('#4338ca') };

export const STEP_CATEGORIES: { label: StepMetaEntry['category']; types: EmailStepType[] }[] = [
  { label: 'Messaging', types: ['SendEmail'] },
  { label: 'Logic', types: ['Wait', 'Condition'] },
  { label: 'Actions', types: ['AddTag', 'RemoveTag', 'AddToList', 'RemoveFromList', 'UpdateSubscriberField', 'Webhook', 'Stop'] },
];

// EmailAutomationEngine.ApplySubscriberField's hardcoded writable-field switch.
export const UPDATE_SUBSCRIBER_FIELD_OPTIONS = ['FirstName', 'LastName', 'Phone', 'Type'] as const;

// EmailAutomationEngine.EvaluateConditionAsync's SubscriberField switch — a real superset of
// UPDATE_SUBSCRIBER_FIELD_OPTIONS above (adds Email, Status), mirrored faithfully rather than
// merged into one "corrected" list, same as the WhatsApp canvas does for its own two field lists.
export const CONDITION_SUBSCRIBER_FIELD_OPTIONS = ['FirstName', 'LastName', 'Email', 'Phone', 'Type', 'Status'] as const;

interface ConditionSubjectHelp {
  showOperand: boolean;
  showValue: boolean;
  operandPlaceholder?: string;
  operandHint?: string;
  valuePlaceholder?: string;
}

// Which of Operand/Value each subject actually reads server-side
// (EmailAutomationEngine.EvaluateConditionAsync). HasTag/HasList only compare a tag/list id (no
// live tag/list picker exists yet — a controller-side integration can upgrade the plain id input
// to a real picker in a later pass), so only SubscriberField uses Value at all.
export const CONDITION_SUBJECT_META: Record<ConditionSubject, ConditionSubjectHelp> = {
  SubscriberField: {
    showOperand: true,
    showValue: true,
    operandPlaceholder: 'Field name',
    operandHint: 'One of: FirstName, LastName, Email, Phone, Type, Status',
    valuePlaceholder: 'Value to match',
  },
  HasTag: { showOperand: true, showValue: false, operandPlaceholder: 'Tag id', operandHint: 'Numeric tag id' },
  HasList: { showOperand: true, showValue: false, operandPlaceholder: 'List id', operandHint: 'Numeric list id' },
};

// Matches EmailMergeTagRenderer's {{subscriber.*}} handling (Services/Email/EmailMergeTagRenderer.cs).
export const PERSONALIZATION_TOKENS: { token: string; label: string }[] = [
  { token: '{{subscriber.firstName}}', label: 'First name' },
  { token: '{{subscriber.fullName}}', label: 'Full name' },
  { token: '{{subscriber.email}}', label: 'Email' },
];
