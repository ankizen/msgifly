// Every field name here mirrors Services/Automations/AutomationDtos.cs verbatim (camelCase on the
// wire, per JsonSerializerOptions.Web) — this is a strict client/server contract, not a convention
// to improve on.

export type StepType =
  | 'SendMessage'
  | 'SendTemplate'
  | 'SendButtons'
  | 'Wait'
  | 'Condition'
  | 'UpdateContactField'
  | 'SendWebhook'
  | 'Stop';

export interface SendMessageConfig {
  text: string;
}

export interface SendTemplateConfig {
  templateName: string;
  language: string;
  headerParam: string | null;
  bodyParams: string[];
}

export interface AutomationButtonConfig {
  id: string;
  title: string;
}

export interface SendButtonsConfig {
  bodyText: string;
  buttons: AutomationButtonConfig[];
}

export interface WaitConfig {
  amount: number;
  unit: 'minutes' | 'hours' | 'days';
}

// Open 4-value string set — the C# ConditionSubject enum only declares 3 members and is missing
// TemplateClicked, a real 4th value both AutomationEngine and the old canvas support. Don't treat
// the enum as authoritative.
export type ConditionSubject = 'MessageContent' | 'ContactField' | 'TimeOfDay' | 'TemplateClicked';

export interface ConditionConfig {
  subject: ConditionSubject;
  operand: string | null;
  value: string | null;
}

export interface UpdateContactFieldConfig {
  field: string;
  value: string;
}

export interface SendWebhookConfig {
  url: string;
  bodyTemplate: string | null;
  headers: Record<string, string> | null;
}

// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export type StopConfig = Record<string, never>;

export type ConfigFor<T extends StepType> = T extends 'SendMessage'
  ? SendMessageConfig
  : T extends 'SendTemplate'
    ? SendTemplateConfig
    : T extends 'SendButtons'
      ? SendButtonsConfig
      : T extends 'Wait'
        ? WaitConfig
        : T extends 'Condition'
          ? ConditionConfig
          : T extends 'UpdateContactField'
            ? UpdateContactFieldConfig
            : T extends 'SendWebhook'
              ? SendWebhookConfig
              : StopConfig;

/** Client-side canonical node — a stable `id` (React Flow node id / React key / selection target)
 * that never reaches the server. Only Condition branches; everything else is a single linear
 * chain link, matching AutomationStep's ParentStepId/Branch persistence model exactly.
 *
 * Built via a mapped-type-then-index trick rather than a plain generic interface: a naive
 * `interface LinearStepNode<T> { type: T; config: ConfigFor<T> }` does NOT produce a real
 * discriminated union when T defaults to the full 7-member union — `type` and `config` each end up
 * typed as their own flat union independently, with no correlation between a specific `type` value
 * and its matching `config` shape, so narrowing on `step.type` in a switch never narrows
 * `step.config`. Mapping over each member individually and then indexing by the same union forces
 * TypeScript to distribute properly, giving 7 genuinely separate object shapes. */
type LinearStepNodeMap = {
  [K in Exclude<StepType, 'Condition'>]: { id: string; type: K; config: ConfigFor<K> };
};
export type LinearStepNode = LinearStepNodeMap[Exclude<StepType, 'Condition'>];

export interface ConditionStepNode {
  id: string;
  type: 'Condition';
  config: ConditionConfig;
  yes: StepNode[];
  no: StepNode[];
}

export type StepNode = LinearStepNode | ConditionStepNode;

/** The exact wire shape AutomationStepNode (Models/ViewModels/AutomationFormViewModel.cs) expects
 * — no id, no position, ever. */
export interface WireStepNode {
  type: StepType;
  config: unknown;
  yes?: WireStepNode[];
  no?: WireStepNode[];
}

export type ConditionMatchType = 'contains' | 'exact' | 'word';

/** Canonical root state — trigger scalar fields flat (matching AutomationFormViewModel's own
 * flat shape) plus the step tree. */
export interface AutomationTree {
  triggerType: string;
  keywords: string;
  matchType: ConditionMatchType;
  caseSensitive: boolean;
  replyIds: string;
  leadFormId: string;
  steps: StepNode[];
}

/** Matches the TemplateOption C# record's camelCase wire shape exactly. */
export interface TemplateOption {
  templateId: string;
  name: string;
  headerFormat: string | null;
  headerParamsCount: number;
  bodyParamsCount: number;
  footerParamsCount: number;
  bodyText: string;
  language: string;
  headerText: string | null;
  headerMediaUrl: string | null;
  footerText: string | null;
  buttonsJson: string | null;
}

export interface LeadForm {
  id: string;
  name: string;
}

/** Matches Save.cshtml's `initial` object literal exactly — the same shape
 * window.createAutomationCanvas already received (lowercase triggerFields keys are Drawflow's old
 * df-* naming convention, preserved here purely so Save.cshtml's init() needs zero changes). */
export interface InitialProps {
  triggerType: string;
  triggerFields: {
    keywords: string;
    matchtype: string;
    casesensitive: 'true' | 'false';
    replyids: string;
    leadformid: string;
  };
  leadForms: LeadForm[];
  templateOptions: TemplateOption[];
  steps: WireStepNode[];
}

/** Matches exportForSubmit()'s existing return shape — copied field-by-field into the 7 hidden
 * inputs by Save.cshtml's submit handler, unchanged. */
export interface ExportResult {
  triggerType: string;
  keywords: string;
  matchType: string;
  caseSensitive: boolean;
  replyIds: string;
  leadFormId: string;
  stepsJson: string;
}

export interface ValidationIssue {
  nodeId: string;
  message: string;
}

export interface OnChangeState {
  sessionWindowWarning: string | null;
  validationIssues: ValidationIssue[];
  isInitialRender: boolean;
}

export interface AutomationBuilderHandle {
  exportForSubmit(): ExportResult;
  focusNode(nodeId: string): void;
  tidyUp(): void;
  zoomIn(): void;
  zoomOut(): void;
  zoomReset(): void;
  destroy(): void;
}
