import type { ReactNode } from 'react';
import { useEffect } from 'react';

interface Props {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
}

/** Generic right-side slide-in panel — the FluentCRM funnel editor's el-drawer (Element Plus)
 * equivalent, hand-rolled since no drawer/sheet component exists elsewhere in this repo. Shared by
 * AddStepDrawer (the block picker) and StepEditorDrawer (the settings form) — the only two call
 * sites, so a shared shell pulls its weight without being a speculative "reusable component". Fixed
 * to the viewport (not the old canvas-local "absolute" panel) since there's no canvas container to
 * dock against anymore. */
export function Drawer({ open, title, onClose, children, footer }: Props) {
  useEffect(() => {
    if (!open) return;
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [open, onClose]);

  return (
    <div className={`fixed inset-0 z-40 ${open ? '' : 'pointer-events-none'}`} aria-hidden={!open}>
      <div
        onClick={onClose}
        className={`absolute inset-0 bg-slate-900/40 transition-opacity duration-200 ${open ? 'opacity-100' : 'opacity-0'}`}
      />
      <div
        className={`absolute top-0 right-0 bottom-0 w-full max-w-md bg-white dark:bg-slate-800 shadow-xl flex flex-col transition-transform duration-200 ease-out ${
          open ? 'translate-x-0' : 'translate-x-full'
        }`}
      >
        <div className="flex items-center gap-2.5 px-4 py-3 border-b border-gray-200 dark:border-slate-700 flex-shrink-0">
          <h3 className="text-sm font-semibold text-gray-900 dark:text-white flex-1 truncate">{title}</h3>
          <button type="button" onClick={onClose} className="text-gray-400 hover:text-gray-600 dark:hover:text-slate-200 text-lg leading-none">
            &times;
          </button>
        </div>
        <div className="flex-1 overflow-y-auto px-4 py-3">{children}</div>
        {footer && <div className="px-4 py-3 border-t border-gray-200 dark:border-slate-700 flex-shrink-0">{footer}</div>}
      </div>
    </div>
  );
}
