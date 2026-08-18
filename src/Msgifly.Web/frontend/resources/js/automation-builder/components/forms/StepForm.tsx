import { useRef } from 'react';
import { CONDITION_SUBJECT_META, UPDATE_CONTACT_FIELD_OPTIONS } from '../../step-meta';
import type { AutomationButtonConfig, ConditionSubject, StepNode, TemplateOption } from '../../types';
import { PersonalizationChips } from './PersonalizationChips';
import { SendTemplateForm } from './SendTemplateForm';

const FIELD_CLASS =
  'mt-1 block w-full rounded-md border-gray-300 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 shadow-sm text-xs focus:border-emerald-500 focus:ring-emerald-500';
const HINT_CLASS = 'text-[10px] leading-tight text-gray-400 dark:text-slate-500 mt-1';
const SESSION_HINT = "⚠ Only works within 24h of the customer's last message — a brand-new contact needs a \"Send Template\" first.";

interface Props {
  step: StepNode;
  templateOptions: TemplateOption[];
  // A generic shallow-merge callback — updateStepConfig on the parent handles the actual typed
  // merge, so the boundary here doesn't need per-type generics.
  onChange: (patch: Record<string, unknown>) => void;
}

export function StepForm({ step, templateOptions, onChange }: Props) {
  const textRef = useRef<HTMLTextAreaElement>(null);
  const bodyTextRef = useRef<HTMLInputElement>(null);

  switch (step.type) {
    case 'SendMessage':
      return (
        <div>
          <textarea
            ref={textRef}
            rows={3}
            value={step.config.text}
            onChange={(e) => onChange({ text: e.target.value })}
            placeholder="Hi {{contact.firstName}}, thanks for..."
            className={FIELD_CLASS}
          />
          <PersonalizationChips targetRef={textRef} onInsert={(v) => onChange({ text: v })} />
          <p className={HINT_CLASS}>{SESSION_HINT}</p>
        </div>
      );

    case 'SendTemplate':
      return <SendTemplateForm config={step.config} templateOptions={templateOptions} onChange={onChange} />;

    case 'SendButtons': {
      const buttons: AutomationButtonConfig[] = [0, 1, 2].map((i) => step.config.buttons[i] ?? { id: '', title: '' });
      function setButton(index: number, patch: Partial<AutomationButtonConfig>) {
        const next = buttons.map((b, i) => (i === index ? { ...b, ...patch } : b)).filter((b) => b.id || b.title);
        // Keep exactly the filled rows — old canvas's export-time filter (drop rows with no id AND
        // no title) is applied live here instead, since there's no separate export step anymore.
        onChange({ buttons: next });
      }
      return (
        <div>
          <input
            ref={bodyTextRef}
            value={step.config.bodyText}
            onChange={(e) => onChange({ bodyText: e.target.value })}
            placeholder="Message body"
            className={FIELD_CLASS}
          />
          <PersonalizationChips targetRef={bodyTextRef} onInsert={(v) => onChange({ bodyText: v })} />
          <div className="grid grid-cols-2 gap-1 mt-1">
            {[0, 1, 2].map((i) => (
              <>
                <input
                  key={`id-${i}`}
                  value={buttons[i].id}
                  onChange={(e) => setButton(i, { id: e.target.value })}
                  placeholder={`Button ${i + 1} id`}
                  className={FIELD_CLASS}
                />
                <input
                  key={`title-${i}`}
                  value={buttons[i].title}
                  onChange={(e) => setButton(i, { title: e.target.value })}
                  placeholder="Label"
                  className={FIELD_CLASS}
                />
              </>
            ))}
          </div>
          <p className={HINT_CLASS}>
            "id" is just a short code for this button that comes back to you when it's tapped (e.g. "yes"/"no") — the customer only ever sees the "Label" text.
          </p>
          <p className={HINT_CLASS}>{SESSION_HINT}</p>
        </div>
      );
    }

    case 'Wait':
      return (
        <div className="flex items-center gap-1">
          <input
            type="number"
            min={1}
            value={step.config.amount}
            onChange={(e) => onChange({ amount: Math.max(1, Number(e.target.value) || 1) })}
            className={`${FIELD_CLASS} w-16`}
          />
          <select value={step.config.unit} onChange={(e) => onChange({ unit: e.target.value })} className={FIELD_CLASS}>
            <option value="minutes">minutes</option>
            <option value="hours">hours</option>
            <option value="days">days</option>
          </select>
        </div>
      );

    case 'Condition': {
      const help = CONDITION_SUBJECT_META[step.config.subject] ?? CONDITION_SUBJECT_META.MessageContent;
      return (
        <div>
          <select value={step.config.subject} onChange={(e) => onChange({ subject: e.target.value as ConditionSubject })} className={FIELD_CLASS}>
            <option value="MessageContent">Message contains</option>
            <option value="ContactField">Contact field equals</option>
            <option value="TimeOfDay">Time of day between</option>
            <option value="TemplateClicked">Last template was clicked</option>
          </select>
          {help.showOperand && (
            <>
              <input
                value={step.config.operand ?? ''}
                onChange={(e) => onChange({ operand: e.target.value })}
                placeholder={help.operandPlaceholder}
                className={FIELD_CLASS}
              />
              {help.operandHint && <p className={HINT_CLASS}>{help.operandHint}</p>}
            </>
          )}
          {help.showValue && (
            <input
              value={step.config.value ?? ''}
              onChange={(e) => onChange({ value: e.target.value })}
              placeholder={help.valuePlaceholder}
              className={FIELD_CLASS}
            />
          )}
          <div className="flex items-center gap-3 mt-1 text-[10px] font-semibold">
            <span className="text-green-600">● Yes</span>
            <span className="text-red-600">● No</span>
          </div>
        </div>
      );
    }

    case 'UpdateContactField':
      return (
        <div>
          <select value={step.config.field} onChange={(e) => onChange({ field: e.target.value })} className={FIELD_CLASS}>
            {UPDATE_CONTACT_FIELD_OPTIONS.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
          <input value={step.config.value} onChange={(e) => onChange({ value: e.target.value })} placeholder="New value" className={FIELD_CLASS} />
        </div>
      );

    case 'SendWebhook':
      return (
        <div>
          <input
            value={step.config.url}
            onChange={(e) => onChange({ url: e.target.value })}
            placeholder="https://your-endpoint.example.com"
            className={FIELD_CLASS}
          />
          <textarea
            rows={2}
            value={step.config.bodyTemplate ?? ''}
            onChange={(e) => onChange({ bodyTemplate: e.target.value })}
            placeholder="Optional JSON body template"
            className={`${FIELD_CLASS} font-mono`}
          />
        </div>
      );

    case 'Stop':
      return <p className="text-[11px] text-gray-400">Ends this branch here.</p>;

    default:
      return null;
  }
}
