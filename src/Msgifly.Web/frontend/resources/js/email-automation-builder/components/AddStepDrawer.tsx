import { useMemo, useState } from 'react';
import { STEP_CATEGORIES, STEP_COLOR, STEP_META } from '../step-meta';
import type { EmailStepType } from '../types';
import { Drawer } from './Drawer';

interface Props {
  open: boolean;
  onPick: (type: EmailStepType) => void;
  onClose: () => void;
}

/** The right-side "Add Action" drawer from the FluentCRM funnel editor (BlockChoice component in
 * Edit.js) — a search box up top plus every step type grouped into categories below, each row
 * showing an icon/title/description. FluentCRM splits Actions/Goals/Conditionals into tabs; Email
 * has no Goals (benchmark) concept and only ~9 step types total, so one flat categorized list
 * (search still narrows it) covers the same ground without a tab bar to maintain. */
export function AddStepDrawer({ open, onPick, onClose }: Props) {
  const [search, setSearch] = useState('');

  const categories = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return STEP_CATEGORIES;
    return STEP_CATEGORIES.map((cat) => ({
      ...cat,
      types: cat.types.filter((t) => STEP_META[t].label.toLowerCase().includes(q) || STEP_META[t].description.toLowerCase().includes(q)),
    })).filter((cat) => cat.types.length > 0);
  }, [search]);

  return (
    <Drawer open={open} title="Add a step" onClose={onClose}>
      <input
        type="text"
        autoFocus
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search steps, e.g. email, tag, wait…"
        className="block w-full rounded-md border-gray-300 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 shadow-sm text-sm focus:border-emerald-500 focus:ring-emerald-500"
      />

      <div className="mt-3 space-y-4">
        {categories.map((cat) => (
          <div key={cat.label}>
            <div className="text-[10px] font-semibold uppercase tracking-wider text-gray-400 dark:text-slate-500 px-1 mb-1">{cat.label}</div>
            <div className="space-y-1">
              {cat.types.map((type) => {
                const color = STEP_COLOR[type];
                const meta = STEP_META[type];
                return (
                  <button
                    key={type}
                    type="button"
                    onClick={() => onPick(type)}
                    className="w-full flex items-start gap-3 rounded-lg border border-gray-200 dark:border-slate-700 px-3 py-2 text-left hover:border-emerald-400 hover:bg-emerald-50/50 dark:hover:bg-slate-700"
                  >
                    <span className="flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center text-sm" style={{ background: color.tint }}>
                      {meta.icon}
                    </span>
                    <span className="min-w-0">
                      <span className="block text-sm font-medium text-gray-900 dark:text-white">{meta.label}</span>
                      <span className="block text-xs text-gray-500 dark:text-slate-400 mt-0.5">{meta.description}</span>
                    </span>
                  </button>
                );
              })}
            </div>
          </div>
        ))}
        {categories.length === 0 && <p className="text-sm text-gray-400 dark:text-slate-500 text-center py-6">No steps found.</p>}
      </div>
    </Drawer>
  );
}
