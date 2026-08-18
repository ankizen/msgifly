import { createRoot } from 'react-dom/client';
import { AutomationBuilderApp } from './components/AutomationBuilderApp';
import type { AutomationBuilderHandle, ExportResult, InitialProps, OnChangeState } from './types';

const EMPTY_EXPORT: ExportResult = {
  triggerType: 'InboundMessage',
  keywords: '',
  matchType: 'contains',
  caseSensitive: false,
  replyIds: '',
  leadFormId: '',
  stepsJson: '[]',
};

/** Drop-in replacement for the old window.createAutomationCanvas(container, initial, onChange) —
 * same 3-arg shape, same initial/onChange/export shapes, so Save.cshtml's surrounding Alpine logic
 * needs no changes beyond the one call-site swap. Returns a stable handle object immediately;
 * React's actual mount happens a tick later, so each method defensively delegates to the inner
 * ref only once it's populated (nothing calls these before first paint in practice). */
export function mountAutomationBuilder(container: HTMLElement, initial: InitialProps, onChange: (state: OnChangeState) => void): AutomationBuilderHandle {
  const root = createRoot(container);
  const inner: { current: AutomationBuilderHandle | null } = { current: null };

  const handle: AutomationBuilderHandle = {
    exportForSubmit: () => inner.current?.exportForSubmit() ?? EMPTY_EXPORT,
    focusNode: (nodeId) => inner.current?.focusNode(nodeId),
    tidyUp: () => inner.current?.tidyUp(),
    zoomIn: () => inner.current?.zoomIn(),
    zoomOut: () => inner.current?.zoomOut(),
    zoomReset: () => inner.current?.zoomReset(),
    destroy: () => {
      inner.current?.destroy();
      root.unmount();
    },
  };

  root.render(
    <AutomationBuilderApp
      ref={(r) => {
        inner.current = r;
      }}
      initial={initial}
      onChange={onChange}
    />
  );

  return handle;
}
