import { useEffect, useRef, useState } from 'react';
import { STEP_CATEGORIES, STEP_COLOR, STEP_META } from '../step-meta';
import type { EmailStepType } from '../types';

export interface PendingInsert {
  screenPos: { x: number; y: number };
}

interface Props {
  pending: PendingInsert;
  onPick: (type: EmailStepType) => void;
  onClose: () => void;
}

/** Hand-rolled categorized type-picker popover, positioned at the triggering click's screen
 * coordinates — no component library exists in this repo to reach for instead. */
export function AddStepMenu({ pending, onPick, onClose }: Props) {
  const ref = useRef<HTMLDivElement>(null);
  // Mounts one frame "closed" then flips open, so the CSS transition below actually has a starting
  // state to animate from instead of popping in fully-formed.
  const [shown, setShown] = useState(false);
  useEffect(() => {
    const raf = requestAnimationFrame(() => setShown(true));
    return () => cancelAnimationFrame(raf);
  }, []);

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

  // Opens to the side of the click, vertically centered on it — not directly below, which in this
  // top-to-bottom flow is exactly where the next step in the chain already sits. Flips to the left
  // of the click near the right edge instead of clamping flush against it, so the menu never
  // overlaps the "+" button (and the node below it) that opened it.
  const MENU_WIDTH = 224;
  const MENU_HEIGHT_EST = 340;
  const left =
    pending.screenPos.x + 16 + MENU_WIDTH <= window.innerWidth
      ? pending.screenPos.x + 16
      : pending.screenPos.x - 16 - MENU_WIDTH;
  const top = Math.min(Math.max(pending.screenPos.y - 60, 8), window.innerHeight - MENU_HEIGHT_EST - 8);

  return (
    <div
      ref={ref}
      style={{ position: 'fixed', left, top, zIndex: 50 }}
      className={`w-56 rounded-xl border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 shadow-xl py-1.5 origin-top-left transition-all duration-100 ${
        shown ? 'opacity-100 scale-100' : 'opacity-0 scale-95'
      }`}
    >
      {STEP_CATEGORIES.map((cat, i) => (
        <div key={cat.label} className={i > 0 ? 'mt-1 pt-1 border-t border-slate-100 dark:border-slate-700' : undefined}>
          <div className="px-3 pt-1 pb-0.5 text-[10px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">{cat.label}</div>
          {cat.types.map((type) => {
            const color = STEP_COLOR[type];
            return (
              <button
                key={type}
                type="button"
                onClick={() => onPick(type)}
                className="w-full flex items-center gap-2.5 px-2.5 py-1.5 text-xs text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700 text-left"
              >
                <span className="flex-shrink-0 w-6 h-6 rounded-full flex items-center justify-center text-[11px]" style={{ background: color.tint }}>
                  {STEP_META[type].icon}
                </span>
                <span>{STEP_META[type].label}</span>
              </button>
            );
          })}
        </div>
      ))}
    </div>
  );
}
