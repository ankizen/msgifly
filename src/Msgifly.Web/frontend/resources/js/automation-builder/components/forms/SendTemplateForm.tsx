import { useRef } from 'react';
import { MAX_BODY_PARAMS } from '../../step-meta';
import type { SendTemplateConfig, TemplateOption } from '../../types';
import { PersonalizationChips } from './PersonalizationChips';
import { TemplatePreviewBubble } from './TemplatePreviewBubble';

const FIELD_CLASS =
  'mt-1 block w-full rounded-md border-gray-300 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 shadow-sm text-xs focus:border-emerald-500 focus:ring-emerald-500';

interface Props {
  config: SendTemplateConfig;
  templateOptions: TemplateOption[];
  onChange: (patch: Partial<SendTemplateConfig>) => void;
}

export function SendTemplateForm({ config, templateOptions, onChange }: Props) {
  const headerRef = useRef<HTMLInputElement>(null);
  const template = templateOptions.find((t) => t.name === config.templateName);
  const hasTextHeader = template?.headerFormat === 'TEXT' && (template?.headerParamsCount ?? 0) > 0;

  function selectTemplate(name: string) {
    const next = templateOptions.find((t) => t.name === name);
    // Switching templates re-slices bodyParams to the newly-selected template's actual count —
    // otherwise a 3-variable template's values would linger as stale hidden data after switching
    // to a 1-variable one.
    const bodyParams = Array.from({ length: next?.bodyParamsCount ?? 0 }, (_, i) => config.bodyParams[i] ?? '');
    onChange({ templateName: name, language: next?.language ?? 'en_US', bodyParams, headerParam: '' });
  }

  function setBodyParam(index: number, value: string) {
    const bodyParams = [...config.bodyParams];
    bodyParams[index] = value;
    onChange({ bodyParams });
  }

  return (
    <div className="space-y-2">
      <select value={config.templateName} onChange={(e) => selectTemplate(e.target.value)} className={FIELD_CLASS}>
        <option value="">Choose a template…</option>
        {templateOptions.map((t) => (
          <option key={t.templateId} value={t.name}>
            {t.name}
          </option>
        ))}
      </select>

      {hasTextHeader && (
        <>
          <input
            ref={headerRef}
            value={config.headerParam ?? ''}
            onChange={(e) => onChange({ headerParam: e.target.value })}
            placeholder="Header value ({{1}})"
            className={FIELD_CLASS}
          />
          <PersonalizationChips targetRef={headerRef} onInsert={(v) => onChange({ headerParam: v })} />
        </>
      )}

      {Array.from({ length: Math.min(template?.bodyParamsCount ?? 0, MAX_BODY_PARAMS) }, (_, i) => (
        <input
          key={i}
          value={config.bodyParams[i] ?? ''}
          onChange={(e) => setBodyParam(i, e.target.value)}
          placeholder={`Value for {{${i + 1}}}`}
          className={FIELD_CLASS}
        />
      ))}

      {template && <TemplatePreviewBubble template={template} headerValue={config.headerParam ?? ''} bodyValues={config.bodyParams} />}
    </div>
  );
}
