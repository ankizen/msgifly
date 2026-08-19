import type { AddTarget, InsertScope } from '../tree';
import type { EmailStepNode } from '../types';
import { StepRow } from './StepRow';

interface Actions {
  onEdit: (id: string) => void;
  onOpenAdd: (target: AddTarget) => void;
  onDelete: (id: string) => void;
  onClone: (id: string) => void;
  onMove: (id: string, direction: 'up' | 'down') => void;
}

interface Props extends Actions {
  steps: EmailStepNode[];
  scope: InsertScope;
}

/** A thin always-visible connector line with a "+" button — sits between/after every block, the
 * direct answer to FluentCRM's handleBlockAdd(index) affordance (Edit.js), just always shown
 * instead of hover-gated since there's no extra state worth tracking for that. */
function AddConnector({ onClick }: { onClick: () => void }) {
  return (
    <div className="relative flex items-center justify-center h-6">
      <div className="absolute inset-y-0 left-1/2 -translate-x-1/2 w-px bg-gray-200 dark:bg-slate-700" />
      <button
        type="button"
        title="Add a step here"
        onClick={onClick}
        className="relative z-[1] w-5 h-5 rounded-full border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-gray-400 hover:text-emerald-600 hover:border-emerald-400 dark:hover:text-emerald-400 flex items-center justify-center text-xs leading-none"
      >
        +
      </button>
    </div>
  );
}

/** The recursive vertical block list — FluentCRM's `.fluentcrm_blocks`/ChildBlocks pattern (Edit.js):
 * a plain top-to-bottom list of cards, no canvas, no node dragging. A Condition step renders its own
 * card via StepRow like any other step, immediately followed by a two-column Yes/No fork
 * (ConditionBranches below) where each column is this exact same component recursing on that
 * branch's own array — so nesting a Condition inside a Condition's branch "just works" without any
 * extra recursion-depth handling. Unlike the old React-Flow build, a Condition does NOT terminate
 * the array's rendering: EmailAutomationTreeBuilder.FlattenTree happily walks siblings positioned
 * after a Condition in the same list (confirmed by reading that file), so this renders them too. */
export function StepList({ steps, scope, ...actions }: Props) {
  if (steps.length === 0) {
    return (
      <button
        type="button"
        onClick={() => actions.onOpenAdd({ kind: 'start', scope })}
        className="w-full rounded-xl border-2 border-dashed border-gray-300 dark:border-slate-600 px-3 py-3 text-xs font-medium text-gray-400 dark:text-slate-500 hover:border-emerald-400 hover:text-emerald-600 dark:hover:text-emerald-400 transition-colors duration-150"
      >
        + Add step
      </button>
    );
  }

  return (
    <div>
      {steps.map((step, index) => (
        <div key={step.id}>
          <StepRow
            step={step}
            isFirst={index === 0}
            isLast={index === steps.length - 1}
            onEdit={() => actions.onEdit(step.id)}
            onDelete={() => actions.onDelete(step.id)}
            onClone={() => actions.onClone(step.id)}
            onMoveUp={() => actions.onMove(step.id, 'up')}
            onMoveDown={() => actions.onMove(step.id, 'down')}
          />
          {step.type === 'Condition' && <ConditionBranches step={step} {...actions} />}
          <AddConnector onClick={() => actions.onOpenAdd({ kind: 'after', anchorId: step.id })} />
        </div>
      ))}
    </div>
  );
}

/** The two-column Yes/No fork directly under a Condition block — FluentCRM's
 * `.block_conditional_wrapper` > `.block_cond_holder.block_cond_no` / `.block_cond_holder
 * .block_cond_yes` (Edit.js), each holding its own recursive block list. FluentCRM draws the
 * connector with a jQuery-measured DomPath component; this is the "ordinary CSS" equivalent the
 * task spec explicitly sanctions instead — plain absolutely-positioned bars forming a bracket down
 * from the Condition card into each column, no pixel measurement needed. */
function ConditionBranches({ step, ...actions }: { step: Extract<EmailStepNode, { type: 'Condition' }> } & Actions) {
  return (
    <div className="relative pt-5 pb-1">
      <div className="absolute left-1/2 top-0 w-px h-5 -translate-x-1/2 bg-gray-300 dark:bg-slate-600" />
      <div className="absolute top-5 left-1/4 right-1/4 h-px bg-gray-300 dark:bg-slate-600" />
      <div className="grid grid-cols-2 gap-4 sm:gap-6">
        <div className="relative pt-5">
          <div className="absolute left-1/2 top-0 w-px h-5 -translate-x-1/2 bg-gray-300 dark:bg-slate-600" />
          <div className="text-center text-[10px] font-bold uppercase tracking-wider mb-2 text-red-500 dark:text-red-400">✕ No</div>
          <StepList steps={step.no} scope={{ kind: 'branch', conditionId: step.id, branch: 'no' }} {...actions} />
        </div>
        <div className="relative pt-5">
          <div className="absolute left-1/2 top-0 w-px h-5 -translate-x-1/2 bg-gray-300 dark:bg-slate-600" />
          <div className="text-center text-[10px] font-bold uppercase tracking-wider mb-2 text-green-600 dark:text-green-400">✓ Yes</div>
          <StepList steps={step.yes} scope={{ kind: 'branch', conditionId: step.id, branch: 'yes' }} {...actions} />
        </div>
      </div>
    </div>
  );
}
