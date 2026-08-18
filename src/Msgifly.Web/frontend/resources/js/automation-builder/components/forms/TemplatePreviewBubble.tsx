import type { TemplateOption } from '../../types';

/** Parses a template's ButtonsJson — Meta serializes buttons differently depending on origin
 * (PascalCase for templates created locally, whatever casing the Graph API itself uses for synced
 * templates), so this reads both instead of assuming one. Malformed/unrecognized JSON just yields
 * no buttons rather than throwing. */
function parseTemplateButtons(buttonsJson: string | null): { type: string; text: string }[] {
  if (!buttonsJson) return [];
  try {
    const raw = JSON.parse(buttonsJson);
    if (!Array.isArray(raw)) return [];
    return raw
      .map((b) => ({ type: b.type || b.Type || '', text: b.text || b.Text || '' }))
      .filter((b) => b.type || b.text);
  } catch {
    return [];
  }
}

interface Props {
  template: TemplateOption;
  headerValue: string;
  bodyValues: string[];
}

/** Same WhatsApp-bubble mockup as the Templates editor's own live preview and the old canvas's
 * renderTemplatePreviewHtml — shows what will actually land in the customer's chat, not just raw
 * body text. */
export function TemplatePreviewBubble({ template, headerValue, bodyValues }: Props) {
  let bodyText = template.bodyText || '';
  for (let n = 1; n <= template.bodyParamsCount; n++) {
    bodyText = bodyText.replaceAll(`{{${n}}}`, bodyValues[n - 1] || `{{${n}}}`);
  }

  const buttons = parseTemplateButtons(template.buttonsJson);

  return (
    <div className="mt-1 rounded-md p-2" style={{ backgroundColor: '#e5ddd5' }}>
      <div className="bg-white rounded-md shadow-sm p-2">
        {template.headerFormat === 'TEXT' && template.headerText && (
          <p className="font-semibold text-[12px] text-gray-900 mb-1">
            {template.headerText.replace(/\{\{1\}\}/g, headerValue || '{{1}}')}
          </p>
        )}
        {template.headerFormat === 'IMAGE' &&
          (template.headerMediaUrl ? (
            <img src={template.headerMediaUrl} className="w-full max-h-28 object-cover rounded mb-1.5" alt="" />
          ) : (
            <div className="w-full h-20 bg-gray-200 rounded flex items-center justify-center text-gray-400 text-[10px] mb-1.5">Image header</div>
          ))}
        {template.headerFormat === 'VIDEO' && (
          <div className="w-full h-20 bg-gray-800 rounded flex items-center justify-center text-white text-[10px] mb-1.5">🎥 Video header</div>
        )}
        {template.headerFormat === 'DOCUMENT' && (
          <div className="w-full py-3 bg-gray-100 rounded flex items-center justify-center text-gray-500 text-[10px] mb-1.5">📄 Document header</div>
        )}
        <p className="text-[12px] text-gray-900 whitespace-pre-wrap">{bodyText || '(this template has no body text)'}</p>
        {template.footerText && <p className="text-[10px] text-gray-500 mt-1">{template.footerText}</p>}
      </div>
      {buttons.map((b, i) => (
        <div key={i} className="border-t border-gray-100 px-2 py-1.5 text-center text-[11px] text-blue-600 bg-white">
          {b.text || b.type}
        </div>
      ))}
    </div>
  );
}
