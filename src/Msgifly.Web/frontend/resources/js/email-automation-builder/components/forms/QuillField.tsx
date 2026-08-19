import { useEffect, useRef } from 'react';
import Quill from 'quill';

interface Props {
  value: string;
  onChange: (html: string) => void;
}

/** Minimal React wrapper around Quill's own vanilla API — no react-quill dependency exists in this
 * repo, and Quill doesn't need one (config.js already loads it globally as window.Quill, plus its
 * snow theme CSS, for the rest of the app; this component just imports the same already-installed
 * `quill` package directly for proper typing). Uncontrolled by design: the editor is seeded from
 * `value` once on mount and never re-synced from props afterward — re-pasting HTML into a live
 * Quill instance on every parent re-render would fight the user's own cursor position while typing. */
export function QuillField({ value, onChange }: Props) {
  // Two-div boundary, not one: Quill's snow theme inserts a toolbar <div> as a raw DOM sibling of
  // whatever element it's given, outside React's own bookkeeping of that element's children. If
  // Quill mounted directly into the div React returns to ITS parent, that untracked sibling would
  // sit alongside whatever else the parent renders — fine until the parent re-renders and React
  // tries to reconcile children it doesn't know exist, which throws. Wrapping Quill's mount point
  // in an outer div that this component NEVER changes the shape of means React only ever has to
  // remove-the-whole-subtree (safe) rather than diff-into-it (unsafe).
  const outerRef = useRef<HTMLDivElement>(null);
  const innerRef = useRef<HTMLDivElement>(null);
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  useEffect(() => {
    if (!innerRef.current) return;
    const quill = new Quill(innerRef.current, {
      theme: 'snow',
      modules: {
        toolbar: [['bold', 'italic', 'underline', 'link'], [{ list: 'ordered' }, { list: 'bullet' }], ['clean']],
      },
    });
    if (value) quill.clipboard.dangerouslyPasteHTML(value);
    quill.on('text-change', () => onChangeRef.current(quill.root.innerHTML));
    // Mount once — see the uncontrolled-by-design note above. No cleanup needed: unmounting
    // discards the whole outerRef subtree (toolbar included) in one operation.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div ref={outerRef}>
      <div ref={innerRef} />
    </div>
  );
}
