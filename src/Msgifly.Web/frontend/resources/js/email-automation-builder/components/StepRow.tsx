import { useEffect, useRef, useState } from 'react';
import { STEP_COLOR, STEP_META } from '../step-meta';
import { summarizeStep } from '../summarize';
import type { EmailStepNode } from '../types';

interface Props {
  step: EmailStepNode;
  isFirst: boolean;
  isLast: boolean;
  onEdit: () => void;
  onDelete: () => void;
  onClone: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
}

/** One block card in the vertical step list — the direct equivalent of FluentCRM's
 * `.fluentcrm_block` (see ChildBlocks/the main funnel_sequences render in Edit.js): an icon, a
 * title, a one-line live description of the step's own settings (summarizeStep), a "⋮" dropdown
 * for Delete/Clone, and up/down reorder buttons disabled at the ends of the list — not
 * drag-and-drop, matching FluentCRM's moveToPosition('up'|'down', index) exactly. Clicking the card
 * body (not the menu, not the arrows) opens the settings drawer. */
export function StepRow({ step, isFirst, isLast, onEdit, onDelete, onClone, onMoveUp, onMoveDown }: Props) {
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const meta = STEP_META[step.type];
  const color = STEP_COLOR[step.type];

  useEffect(() => {
    if (!menuOpen) return;
    function handleClick(e: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [menuOpen]);

  return (
    <div
      data-step-id={step.id}
      onClick={onEdit}
      className="group relative flex items-start gap-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 px-3.5 py-3 cursor-pointer shadow-sm hover:shadow-md hover:border-emerald-300 dark:hover:border-emerald-700 transition-all duration-150"
    >
      <span className="flex-shrink-0 w-9 h-9 rounded-full flex items-center justify-center text-base" style={{ background: color.tint }}>
        {meta.icon}
      </span>
      <div className="min-w-0 flex-1 pr-6">
        <div className="text-sm font-semibold text-gray-900 dark:text-white truncate">{meta.label}</div>
        <div className="text-xs text-gray-500 dark:text-slate-400 truncate mt-0.5">{summarizeStep(step)}</div>
      </div>

      <div className="flex-shrink-0 flex items-center gap-0.5">
        <button
          type="button"
          title="Move up"
          disabled={isFirst}
          onClick={(e) => {
            e.stopPropagation();
            onMoveUp();
          }}
          className="w-6 h-6 flex items-center justify-center rounded text-gray-400 hover:text-gray-700 hover:bg-gray-100 dark:hover:bg-slate-700 dark:hover:text-slate-200 disabled:opacity-25 disabled:pointer-events-none"
        >
          ▲
        </button>
        <button
          type="button"
          title="Move down"
          disabled={isLast}
          onClick={(e) => {
            e.stopPropagation();
            onMoveDown();
          }}
          className="w-6 h-6 flex items-center justify-center rounded text-gray-400 hover:text-gray-700 hover:bg-gray-100 dark:hover:bg-slate-700 dark:hover:text-slate-200 disabled:opacity-25 disabled:pointer-events-none"
        >
          ▼
        </button>

        <div ref={menuRef} className="relative">
          <button
            type="button"
            title="More"
            onClick={(e) => {
              e.stopPropagation();
              setMenuOpen((v) => !v);
            }}
            className="w-6 h-6 flex items-center justify-center rounded text-gray-400 hover:text-gray-700 hover:bg-gray-100 dark:hover:bg-slate-700 dark:hover:text-slate-200"
          >
            ⋮
          </button>
          {menuOpen && (
            <div
              onClick={(e) => e.stopPropagation()}
              className="absolute right-0 top-7 z-10 w-32 rounded-md border border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-800 shadow-lg py-1"
            >
              <button
                type="button"
                onClick={() => {
                  setMenuOpen(false);
                  onClone();
                }}
                className="w-full text-left px-3 py-1.5 text-xs text-gray-700 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700"
              >
                Clone
              </button>
              <button
                type="button"
                onClick={() => {
                  setMenuOpen(false);
                  onDelete();
                }}
                className="w-full text-left px-3 py-1.5 text-xs text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20"
              >
                Delete
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
