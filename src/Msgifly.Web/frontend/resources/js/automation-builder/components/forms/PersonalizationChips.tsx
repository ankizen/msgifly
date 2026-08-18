import { PERSONALIZATION_TOKENS } from '../../step-meta';

interface Props {
  /** Ref to the text field this chip row inserts into. */
  targetRef: React.RefObject<HTMLTextAreaElement | HTMLInputElement | null>;
  onInsert: (newValue: string) => void;
}

/** Inserts a {{contact.*}} token at the cursor position of the associated field (not just
 * appended), matching AutomationEngine.Interpolate's supported placeholders — so filling in
 * "Thank you for submitting, {name}" doesn't require remembering or typing the {{...}} syntax. */
export function PersonalizationChips({ targetRef, onInsert }: Props) {
  function insert(token: string) {
    const field = targetRef.current;
    if (!field) return;
    const start = field.selectionStart ?? field.value.length;
    const end = field.selectionEnd ?? field.value.length;
    const next = field.value.slice(0, start) + token + field.value.slice(end);
    onInsert(next);
    requestAnimationFrame(() => {
      field.focus();
      const cursor = start + token.length;
      field.setSelectionRange(cursor, cursor);
    });
  }

  return (
    <div className="flex flex-wrap gap-1 mt-1">
      {PERSONALIZATION_TOKENS.map((t) => (
        <button
          key={t.token}
          type="button"
          onClick={() => insert(t.token)}
          className="text-[10px] leading-tight px-1.5 py-0.5 rounded-full bg-emerald-50 dark:bg-emerald-900 text-emerald-700 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-700 hover:bg-emerald-100 dark:hover:bg-emerald-800"
        >
          + {t.label}
        </button>
      ))}
    </div>
  );
}
