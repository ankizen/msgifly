import { useEffect } from 'react';

interface Props {
  message: string;
  confirmLabel?: string;
  onConfirm: () => void;
  onCancel: () => void;
}

/** Replaces window.confirm() for step deletion — the native browser dialog blocks the whole page,
 * can't be styled, and visually clashes with everything else in this panel. Escape cancels, Enter
 * confirms, matching what a native confirm() would do. */
export function ConfirmDialog({ message, confirmLabel = 'Delete', onConfirm, onCancel }: Props) {
  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onCancel();
      if (e.key === 'Enter') onConfirm();
    }
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [onConfirm, onCancel]);

  return (
    <div className="absolute inset-0 z-50 flex items-center justify-center bg-slate-900/40" onClick={onCancel}>
      <div
        onClick={(e) => e.stopPropagation()}
        className="w-80 rounded-xl bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 shadow-xl p-4"
      >
        <p className="text-sm text-slate-700 dark:text-slate-200">{message}</p>
        <div className="mt-4 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            className="px-3 py-1.5 rounded-md border border-slate-300 dark:border-slate-600 text-xs font-medium text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={onConfirm}
            className="px-3 py-1.5 rounded-md bg-red-600 hover:bg-red-500 text-xs font-medium text-white"
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
