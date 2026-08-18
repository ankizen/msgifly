interface Props {
  onTidyUp: () => void;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onZoomReset: () => void;
}

const BTN_CLASS = 'px-2 py-1 rounded-md border border-gray-300 dark:border-slate-600 text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700';

export function Toolbar({ onTidyUp, onZoomIn, onZoomOut, onZoomReset }: Props) {
  return (
    <div className="absolute top-2 right-2 z-10 flex items-center gap-1 bg-white/90 dark:bg-slate-800/90 backdrop-blur rounded-md p-1 shadow">
      <button type="button" onClick={onTidyUp} className={`${BTN_CLASS} text-xs`} title="Auto-arrange the steps">
        Tidy up
      </button>
      <button type="button" onClick={onZoomOut} className={BTN_CLASS} title="Zoom out">
        &minus;
      </button>
      <button type="button" onClick={onZoomReset} className={`${BTN_CLASS} text-xs`}>
        Reset
      </button>
      <button type="button" onClick={onZoomIn} className={BTN_CLASS} title="Zoom in">
        +
      </button>
    </div>
  );
}
