interface Props {
  onTidyUp: () => void;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onZoomReset: () => void;
}

const ZOOM_BTN = 'w-7 h-7 flex items-center justify-center text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 first:rounded-l-md last:rounded-r-md';

export function Toolbar({ onTidyUp, onZoomIn, onZoomOut, onZoomReset }: Props) {
  return (
    <div className="absolute top-3 right-3 z-10 flex items-center gap-2">
      <button
        type="button"
        onClick={onTidyUp}
        title="Auto-arrange the steps"
        className="px-3 h-8 rounded-md border border-slate-200 dark:border-slate-600 bg-white/95 dark:bg-slate-800/95 backdrop-blur text-xs font-medium text-slate-600 dark:text-slate-300 shadow-sm hover:bg-slate-50 dark:hover:bg-slate-700"
      >
        ✨ Tidy up
      </button>
      <div className="flex items-center rounded-md border border-slate-200 dark:border-slate-600 bg-white/95 dark:bg-slate-800/95 backdrop-blur shadow-sm divide-x divide-slate-200 dark:divide-slate-600">
        <button type="button" onClick={onZoomOut} className={ZOOM_BTN} title="Zoom out">
          &minus;
        </button>
        <button type="button" onClick={onZoomReset} className={`${ZOOM_BTN} text-[10px] font-medium w-9`} title="Fit to screen">
          Fit
        </button>
        <button type="button" onClick={onZoomIn} className={ZOOM_BTN} title="Zoom in">
          +
        </button>
      </div>
    </div>
  );
}
