import { createRoot } from 'react-dom/client';
import { EmailAutomationBuilderApp } from './components/EmailAutomationBuilderApp';
import type { EmailAutomationBuilderHandle, ExportResult, InitialProps, OnChangeState } from './types';

const EMPTY_EXPORT: ExportResult = {
  triggerType: 'SubscriberAdded',
  listId: null,
  tagId: null,
  stepsJson: '[]',
};

/** window.loadEmailAutomationBuilder(container, initial, onChange) — same 3-arg shape as the
 * WhatsApp canvas's mountAutomationBuilder, so a future Save.cshtml's Alpine component can call
 * this identically. Returns a stable handle object immediately; React's actual mount happens a tick
 * later, so each method defensively delegates to the inner ref only once it's populated. */
export function mountEmailAutomationBuilder(container: HTMLElement, initial: InitialProps, onChange: (state: OnChangeState) => void): EmailAutomationBuilderHandle {
  const root = createRoot(container);
  const inner: { current: EmailAutomationBuilderHandle | null } = { current: null };

  const handle: EmailAutomationBuilderHandle = {
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
    <EmailAutomationBuilderApp
      ref={(r) => {
        inner.current = r;
      }}
      initial={initial}
      onChange={onChange}
    />
  );

  return handle;
}
