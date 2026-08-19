import { useRef } from 'react';
import { CONDITION_SUBJECT_META, UPDATE_SUBSCRIBER_FIELD_OPTIONS } from '../../step-meta';
import type { ConditionSubject, EmailStepNode } from '../../types';
import { PersonalizationChips } from './PersonalizationChips';
import { QuillField } from './QuillField';

const FIELD_CLASS =
  'mt-1 block w-full rounded-md border-gray-300 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 shadow-sm text-xs focus:border-emerald-500 focus:ring-emerald-500';
const HINT_CLASS = 'text-[10px] leading-tight text-gray-400 dark:text-slate-500 mt-1';

interface Props {
  step: EmailStepNode;
  // A generic shallow-merge callback — updateStepConfig on the parent handles the actual typed
  // merge, so the boundary here doesn't need per-type generics.
  onChange: (patch: Record<string, unknown>) => void;
}

export function StepForm({ step, onChange }: Props) {
  const subjectRef = useRef<HTMLInputElement>(null);

  switch (step.type) {
    case 'SendEmail':
      return (
        <div>
          <label className="text-[10px] font-semibold uppercase tracking-wider text-gray-400 dark:text-slate-500">Subject</label>
          <input
            ref={subjectRef}
            value={step.config.subject}
            onChange={(e) => onChange({ subject: e.target.value })}
            placeholder="Thanks for joining, {{subscriber.firstName}}!"
            className={FIELD_CLASS}
          />
          <PersonalizationChips targetRef={subjectRef} onInsert={(v) => onChange({ subject: v })} />

          <label className="mt-3 block text-[10px] font-semibold uppercase tracking-wider text-gray-400 dark:text-slate-500">Body</label>
          <div className="mt-1">
            <QuillField value={step.config.bodyHtml} onChange={(html) => onChange({ bodyHtml: html })} />
          </div>
        </div>
      );

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
      const help = CONDITION_SUBJECT_META[step.config.subject] ?? CONDITION_SUBJECT_META.SubscriberField;
      return (
        <div>
          <select value={step.config.subject} onChange={(e) => onChange({ subject: e.target.value as ConditionSubject })} className={FIELD_CLASS}>
            <option value="SubscriberField">Subscriber field equals</option>
            <option value="HasTag">Subscriber has tag</option>
            <option value="HasList">Subscriber is on list</option>
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

    case 'AddTag':
    case 'RemoveTag':
      return (
        <div>
          <input
            type="number"
            min={0}
            value={step.config.tagId || ''}
            onChange={(e) => onChange({ tagId: Number(e.target.value) || 0 })}
            placeholder="Tag id"
            className={FIELD_CLASS}
          />
          <p className={HINT_CLASS}>No tag picker yet — enter the tag's numeric id directly.</p>
        </div>
      );

    case 'AddToList':
    case 'RemoveFromList':
      return (
        <div>
          <input
            type="number"
            min={0}
            value={step.config.listId || ''}
            onChange={(e) => onChange({ listId: Number(e.target.value) || 0 })}
            placeholder="List id"
            className={FIELD_CLASS}
          />
          <p className={HINT_CLASS}>No list picker yet — enter the list's numeric id directly.</p>
        </div>
      );

    case 'UpdateSubscriberField':
      return (
        <div>
          <select value={step.config.field} onChange={(e) => onChange({ field: e.target.value })} className={FIELD_CLASS}>
            {UPDATE_SUBSCRIBER_FIELD_OPTIONS.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
          <input value={step.config.value} onChange={(e) => onChange({ value: e.target.value })} placeholder="New value" className={FIELD_CLASS} />
        </div>
      );

    case 'Webhook':
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
