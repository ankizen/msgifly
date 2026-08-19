// Every field name here mirrors Services/EmailAutomations/EmailAutomationDtos.cs and
// EmailAutomationFormViewModel.cs verbatim (camelCase on the wire, per JsonSerializerOptions.Web)
// — this is a strict client/server contract, not a convention to improve on. Independent copy of
// the WhatsApp canvas's types.ts shape, adapted to Email's own (smaller) step/trigger vocabulary —
// no shared type between the two stacks, matching the engine split itself.

export type EmailStepType =
  | 'SendEmail'
  | 'Wait'
  | 'Condition'
  | 'AddTag'
  | 'RemoveTag'
  | 'AddToList'
  | 'RemoveFromList'
  | 'UpdateSubscriberField'
  | 'Webhook'
  | 'Stop';

export type EmailTriggerType = 'SubscriberAdded' | 'TagApplied' | 'ListApplied';

export interface SendEmailConfig {
  subject: string;
  bodyHtml: string;
}

export interface WaitConfig {
  amount: number;
  unit: 'minutes' | 'hours' | 'days';
}

// EmailConditionStepConfig.Subject — SubscriberField | HasTag | HasList.
export type ConditionSubject = 'SubscriberField' | 'HasTag' | 'HasList';

export interface ConditionConfig {
  subject: ConditionSubject;
  operand?: string;
  value?: string;
}

// TagStepConfig — shared by AddTag/RemoveTag.
export interface TagRefConfig {
  tagId: number;
}

// ListStepConfig — shared by AddToList/RemoveFromList.
export interface ListRefConfig {
  listId: number;
}

export interface UpdateSubscriberFieldConfig {
  field: string;
  value: string;
}

export interface WebhookConfig {
  url: string;
  bodyTemplate?: string;
  headers?: Record<string, string>;
}

// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export type StopConfig = Record<string, never>;

export type ConfigFor<T extends EmailStepType> = T extends 'SendEmail'
  ? SendEmailConfig
  : T extends 'Wait'
    ? WaitConfig
    : T extends 'Condition'
      ? ConditionConfig
      : T extends 'AddTag' | 'RemoveTag'
        ? TagRefConfig
        : T extends 'AddToList' | 'RemoveFromList'
          ? ListRefConfig
          : T extends 'UpdateSubscriberField'
            ? UpdateSubscriberFieldConfig
            : T extends 'Webhook'
              ? WebhookConfig
              : StopConfig;

/** Client-side canonical node — a stable `id` (React Flow node id / React key / selection target)
 * that never reaches the server. Only Condition branches; everything else is a single linear chain
 * link, matching EmailAutomationStep's ParentStepId/Branch persistence model exactly. Uses the same
 * mapped-type-then-index trick as the WhatsApp canvas's StepNode (see its types.ts for why a plain
 * generic interface fails to produce a real discriminated union here). */
type LinearStepNodeMap = {
  [K in Exclude<EmailStepType, 'Condition'>]: { id: string; type: K; config: ConfigFor<K> };
};
export type LinearStepNode = LinearStepNodeMap[Exclude<EmailStepType, 'Condition'>];

export interface ConditionStepNode {
  id: string;
  type: 'Condition';
  config: ConditionConfig;
  yes: EmailStepNode[];
  no: EmailStepNode[];
}

export type EmailStepNode = LinearStepNode | ConditionStepNode;

/** The exact wire shape EmailAutomationStepNode (Services/EmailAutomations/EmailAutomationDtos.cs)
 * expects — no id, no position, ever. */
export interface WireStepNode {
  type: EmailStepType;
  config: unknown;
  yes?: WireStepNode[];
  no?: WireStepNode[];
}

/** Canonical root state. Unlike the WhatsApp canvas's AutomationTree (five trigger-specific fields
 * for five trigger types), Email only ever needs two — listId (SubscriberAdded/ListApplied) and
 * tagId (TagApplied) — both kept flat here regardless of which trigger is active, same "unused
 * field just stays null" pattern EmailAutomationFormViewModel itself uses. */
export interface EmailAutomationTree {
  triggerType: EmailTriggerType;
  listId: number | null;
  tagId: number | null;
  steps: EmailStepNode[];
}

/** Matches what a future Save.cshtml passes as `initial` — EmailAutomationFormViewModel's own
 * TriggerType/ListId/TagId/StepsJson fields, JSON-parsed. No leadForms/templateOptions equivalent:
 * Email has no lookup-driven picker in this pass (tag/list ids are plain inputs — see
 * step-meta.ts). No lowercase triggerFields wrapper either — that shape on the WhatsApp side exists
 * purely for old-Drawflow wire compatibility, which has no Email equivalent to preserve. */
export interface InitialProps {
  triggerType: EmailTriggerType;
  listId: number | null;
  tagId: number | null;
  steps: WireStepNode[];
}

/** Matches exportForSubmit()'s return shape — copied field-by-field into hidden inputs binding to
 * EmailAutomationFormViewModel's TriggerType/ListId/TagId/StepsJson properties. */
export interface ExportResult {
  triggerType: EmailTriggerType;
  listId: number | null;
  tagId: number | null;
  stepsJson: string;
}

export interface ValidationIssue {
  nodeId: string;
  message: string;
}

export interface OnChangeState {
  validationIssues: ValidationIssue[];
  isInitialRender: boolean;
}

/** zoomIn/zoomOut/zoomReset/tidyUp from the old React-Flow-canvas handle are gone — there's no
 * pan/zoom surface left to operate on now that this renders as a plain scrolling block list, and
 * Save.cshtml never called them (it only calls exportForSubmit/focusNode). */
export interface EmailAutomationBuilderHandle {
  exportForSubmit(): ExportResult;
  focusNode(nodeId: string): void;
  destroy(): void;
}
