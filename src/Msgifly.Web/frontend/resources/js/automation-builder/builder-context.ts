import { createContext, useContext } from 'react';
import type { InsertScope } from './tree';

export interface BuilderActions {
  selectedId: string | null;
  onSelectNode: (id: string) => void;
  /** Opens the type picker anchored near the click, inserting via insertAfterNode on pick. */
  onAddAfter: (anchorId: string, event: React.MouseEvent) => void;
  /** Opens the type picker anchored near the click, inserting via insertAtScopeStart on pick —
   * used only by EmptySlotNode (an empty Condition branch has nothing to hang an "insert after"
   * affordance off of). */
  onAddAtSlot: (scope: InsertScope, event: React.MouseEvent) => void;
  onDelete: (id: string) => void;
}

export const BuilderActionsContext = createContext<BuilderActions | null>(null);

export function useBuilderActions(): BuilderActions {
  const ctx = useContext(BuilderActionsContext);
  if (!ctx) throw new Error('useBuilderActions must be used within BuilderActionsContext.Provider');
  return ctx;
}
