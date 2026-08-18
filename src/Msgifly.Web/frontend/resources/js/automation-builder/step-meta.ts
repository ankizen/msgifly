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
