import { useEffect, useRef } from 'react';
import { STEP_CATEGORIES, STEP_META } from '../step-meta';
import type { StepType } from '../types';

export interface PendingInsert {
  screenPos: { x: number; y: number };
}

interface Props {
  pending: PendingInsert;
  onPick: (type: StepType) => void;
  onClose: () => void;
}

/** Hand-rolled categorized type-picker popover, positioned at the triggering click's screen
 * coordinates — no component library exists in this repo to reach for instead. */
export function AddStepMenu({ pending, onPick, onClose }: Props) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) onClose();
    }
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.addEventListener('mousedown', handleClick);
    document.addEventListener('keydown', handleKey);
    return () => {
      document.removeEventListener('mousedown', handleClick);
      document.removeEventListener('keydown', handleKey);
    };
  }, [onClose]);

  // Clamp so the menu doesn't render off the right/bottom edge of the viewport.
  const left = Math.min(pending.screenPos.x, window.innerWidth - 220);
  const top = Math.min(pending.screenPos.y, window.innerHeight - 280);

  return (
    <div
      ref={ref}
      style={{ position: 'fixed', left, top, zIndex: 50 }}
      className="w-52 rounded-md border border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-800 shadow-lg py-1"
    >
      {STEP_CATEGORIES.map((cat) => (
        <div key={cat.label}>
          <div className="px-3 pt-1.5 pb-0.5 text-[10px] font-semibold uppercase tracking-wider text-gray-400 dark:text-slate-500">{cat.label}</div>
          {cat.types.map((type) => (
            <button
              key={type}
              type="button"
              onClick={() => onPick(type)}
              className="w-full flex items-center gap-2 px-3 py-1.5 text-xs text-gray-700 dark:text-slate-300 hover:bg-emerald-50 dark:hover:bg-slate-700 text-left"
            >
              <span>{STEP_META[type].icon}</span>
              <span>{STEP_META[type].label}</span>
            </button>
          ))}
        </div>
      ))}
    </div>
  );
}
