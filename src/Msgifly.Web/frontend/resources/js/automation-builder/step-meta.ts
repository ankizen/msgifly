import type { ConditionSubject, StepType } from './types';

// Matches TemplateFormViewModel.MaxBodyVars server-side — WhatsApp templates in this app never
// have more than this many {{n}} body variables.
export const MAX_BODY_PARAMS = 6;

export interface StepMetaEntry {
  label: string;
  icon: string;
  category: 'Messaging' | 'Logic' | 'Actions';
}

// Ports the old canvas's STEP_DEFS labels/icons — same emoji set, no icon library added for this.
export const STEP_META: Record<StepType, StepMetaEntry> = {
  SendMessage: { label: 'Send Message', icon: '💬', category: 'Messaging' },
  SendTemplate: { label: 'Send Template', icon: '📄', category: 'Messaging' },
  SendButtons: { label: 'Send Buttons', icon: '🔘', category: 'Messaging' },
  Condition: { label: 'Condition (if/else)', icon: '🔀', category: 'Logic' },
  Wait: { label: 'Wait', icon: '⏱', category: 'Logic' },
  UpdateContactField: { label: 'Update Contact Field', icon: '✏️', category: 'Actions' },
  SendWebhook: { label: 'Call Webhook', icon: '🌐', category: 'Actions' },
  Stop: { label: 'Stop', icon: '⏹', category: 'Actions' },
};

export interface StepColor {
  /** Solid accent — icon badge background, left rail, ring on selection, edge/handle tint. */
  accent: string;
  /** Pale tint for the icon badge's own background so the accent icon sits on a soft chip. */
  tint: string;
}

// One distinct hue per step type (not category) — a canvas full of same-colored cards is the
// single biggest reason a flow builder reads as flat/lifeless at a glance. Applied via inline
// style, not Tailwind color classes: Tailwind's JIT scanner needs statically-visible class names,
// so a dynamically-picked "bg-{color}-500" would silently never generate.
export const STEP_COLOR: Record<StepType, StepColor> = {
  SendMessage: { accent: '#2563eb', tint: '#eff6ff' },
  SendTemplate: { accent: '#7c3aed', tint: '#f5f3ff' },
  SendButtons: { accent: '#0d9488', tint: '#f0fdfa' },
  Condition: { accent: '#ea580c', tint: '#fff7ed' },
  Wait: { accent: '#d97706', tint: '#fffbeb' },
  UpdateContactField: { accent: '#db2777', tint: '#fdf2f8' },
  SendWebhook: { accent: '#0891b2', tint: '#ecfeff' },
  Stop: { accent: '#dc2626', tint: '#fef2f2' },
};

// The flow's origin gets its own distinct treatment (indigo) — never reused by a step color above.
export const TRIGGER_COLOR: StepColor = { accent: '#4338ca', tint: '#eef2ff' };

export const STEP_CATEGORIES: { label: StepMetaEntry['category']; types: StepType[] }[] = [
  { label: 'Messaging', types: ['SendMessage', 'SendTemplate', 'SendButtons'] },
  { label: 'Logic', types: ['Condition', 'Wait'] },
  { label: 'Actions', types: ['UpdateContactField', 'SendWebhook', 'Stop'] },
];

// AutomationEngine.ApplyContactField's hardcoded writable-field switch — no API exposes this list.
export const UPDATE_CONTACT_FIELD_OPTIONS = ['FirstName', 'LastName', 'Company', 'Email', 'Description', 'City', 'State'] as const;

// AutomationEngine.EvaluateConditionAsync's ContactField switch — deliberately NOT the same list
// as UPDATE_CONTACT_FIELD_OPTIONS above (adds Type, drops Description). A real server-side
// inconsistency, mirrored faithfully rather than merged into one "corrected" list.
export const CONDITION_CONTACT_FIELD_OPTIONS = ['FirstName', 'LastName', 'Company', 'Email', 'City', 'State', 'Type'] as const;

interface ConditionSubjectHelp {
  showOperand: boolean;
  showValue: boolean;
  operandPlaceholder?: string;
  operandHint?: string;
  valuePlaceholder?: string;
}

// Which of Operand/Value each subject actually reads server-side (AutomationEngine.EvaluateConditionAsync).
export const CONDITION_SUBJECT_META: Record<ConditionSubject, ConditionSubjectHelp> = {
  MessageContent: { showOperand: false, showValue: true, valuePlaceholder: 'Text to look for (e.g. price)' },
  ContactField: {
    showOperand: true,
    showValue: true,
    operandPlaceholder: 'Field name',
    operandHint: 'One of: FirstName, LastName, Company, Email, City, State, Type',
    valuePlaceholder: 'Value to match',
  },
  TimeOfDay: {
    showOperand: true,
    showValue: false,
    operandPlaceholder: 'e.g. 09:00-18:00',
    operandHint: '24-hour time range (start-end)',
  },
  TemplateClicked: { showOperand: false, showValue: false },
};

// Matches AutomationEngine.Interpolate's {{contact.*}} handling.
export const PERSONALIZATION_TOKENS: { token: string; label: string }[] = [
  { token: '{{contact.firstName}}', label: 'First name' },
  { token: '{{contact.fullName}}', label: 'Full name' },
  { token: '{{contact.phone}}', label: 'Phone' },
];

// WhatsApp only allows a *template* message to someone with no open 24-hour session — the state of
// a brand-new Lead Ads contact or any freshly-created contact who hasn't messaged in.
export const NO_SESSION_TRIGGERS = new Set(['FacebookLeadReceived', 'NewContactCreated']);
export const SESSION_RESTRICTED_STEPS = new Set<StepType>(['SendButtons', 'SendMessage']);
