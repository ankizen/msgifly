import Drawflow from 'drawflow';
import 'drawflow/dist/drawflow.min.css';

// ------------------------------------------------------------
// Node templates — one per AutomationStepType (+ a special Trigger node).
// df-* attributes on inputs/textareas/selects auto-sync with the node's
// `data` object (Drawflow's own binding mechanism) — no manual wiring needed.
//
// IMPORTANT: HTML attribute names are case-insensitive (the browser lowercases
// them on parse), so a `df-templateName` attribute is indistinguishable from
// `df-templatename` once it's in the DOM — Drawflow's binding ends up writing
// to `data.templatename`, not `data.templateName`. Every df-* name below is
// therefore deliberately all-lowercase, single-word-looking, with NO internal
// capitals. flatConfig()/unflatConfig() are the one place that translates
// between these lowercase canvas-internal names and the server's real
// (camelCase) JSON property names.
// ------------------------------------------------------------

const FIELD_CLASS =
  'mt-1 block w-full rounded-md border-gray-300 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 shadow-sm text-xs focus:border-emerald-500 focus:ring-emerald-500';

function node(title, icon, bodyHtml) {
  return `<div class="df-node-card"><div class="df-node-title">${icon} ${title}</div><div class="df-node-body">${bodyHtml}</div></div>`;
}

// Matches AutomationEngine.Interpolate's {{contact.*}} handling — surfaced as clickable chips
// (not just documentation) so filling in "Thank you for submitting, {name}" doesn't require a
// non-technical admin to remember or type the {{...}} syntax themselves.
const PERSONALIZATION_TOKENS = [
  { token: '{{contact.firstName}}', label: 'First name' },
  { token: '{{contact.fullName}}', label: 'Full name' },
  { token: '{{contact.phone}}', label: 'Phone' },
];

// escapeHtml is defined further below in this file — safe to call here since function
// declarations (unlike const arrow functions) are hoisted for the whole module.
function personalizationChipsHtml() {
  const chips = PERSONALIZATION_TOKENS.map(
    (t) => `<button type="button" class="df-chip" data-token="${escapeHtml(t.token)}">+ ${t.label}</button>`
  ).join('');
  return `<div class="df-chip-row">${chips}</div>`;
}

const STEP_DEFS = {
  SendMessage: {
    label: 'Send Message',
    icon: '💬',
    inputs: 1,
    outputs: 1,
    data: { text: '' },
    html: node(
      'Send Message',
      '💬',
      `<textarea df-text rows="3" placeholder="Hi {{contact.firstName}}, thanks for..." class="${FIELD_CLASS}"></textarea>${personalizationChipsHtml()}
       <p class="df-hint">⚠ Only works within 24h of the customer's last message — a brand-new contact needs a "Send Template" first.</p>`
    ),
  },
  SendTemplate: {
    label: 'Send Template',
    icon: '📄',
    inputs: 1,
    outputs: 1,
    // html is built per-node by sendTemplateHtml(templateOptions) instead of a fixed string here
    // (see addStepNode) — it needs the workspace's actual approved templates to render a picker.
    data: {
      templatename: '',
      language: 'en_US',
      headerparam: '',
      bodyparam1: '',
      bodyparam2: '',
      bodyparam3: '',
      bodyparam4: '',
      bodyparam5: '',
      bodyparam6: '',
    },
    html: null,
  },
  SendButtons: {
    label: 'Send Buttons',
    icon: '🔘',
    inputs: 1,
    outputs: 1,
    data: { bodytext: '', button1id: '', button1title: '', button2id: '', button2title: '', button3id: '', button3title: '' },
    html: node(
      'Send Buttons',
      '🔘',
      `<input df-bodytext placeholder="Message body" class="${FIELD_CLASS}" />
       <div class="grid grid-cols-2 gap-1 mt-1">
         <input df-button1id placeholder="Button 1 id" class="${FIELD_CLASS}" /><input df-button1title placeholder="Label" class="${FIELD_CLASS}" />
         <input df-button2id placeholder="Button 2 id" class="${FIELD_CLASS}" /><input df-button2title placeholder="Label" class="${FIELD_CLASS}" />
         <input df-button3id placeholder="Button 3 id" class="${FIELD_CLASS}" /><input df-button3title placeholder="Label" class="${FIELD_CLASS}" />
       </div>
       <p class="df-hint">"id" is just a short code for this button that comes back to you when it's tapped (e.g. "yes"/"no") — the customer only ever sees the "Label" text.</p>
       <p class="df-hint">⚠ Only works within 24h of the customer's last message — a brand-new contact needs a "Send Template" first.</p>`
    ),
  },
  Wait: {
    label: 'Wait',
    icon: '⏱',
    inputs: 1,
    outputs: 1,
    data: { amount: 1, unit: 'minutes' },
    html: node(
      'Wait',
      '⏱',
      `<div class="flex items-center gap-1">
         <input df-amount type="number" min="1" value="1" class="${FIELD_CLASS} w-16" />
         <select df-unit class="${FIELD_CLASS}">
           <option value="minutes">minutes</option>
           <option value="hours">hours</option>
           <option value="days">days</option>
         </select>
       </div>`
    ),
  },
  Condition: {
    label: 'Condition',
    icon: '🔀',
    inputs: 1,
    outputs: 2,
    data: { subject: 'MessageContent', operand: '', value: '' },
    html: node(
      'Condition (if/else)',
      '🔀',
      `<select df-subject class="${FIELD_CLASS}">
         <option value="MessageContent">Message contains</option>
         <option value="ContactField">Contact field equals</option>
         <option value="TimeOfDay">Time of day between</option>
         <option value="TemplateClicked">Last template was clicked</option>
       </select>
       <input df-operand placeholder="Field name / HH:mm-HH:mm (if needed)" class="${FIELD_CLASS} df-condition-operand" />
       <p class="df-hint df-condition-operand-hint"></p>
       <input df-value placeholder="Value to compare" class="${FIELD_CLASS} df-condition-value" />
       <div class="df-condition-legend">
         <span class="text-green-600">● Yes</span>
         <span class="text-red-600">● No</span>
       </div>`
    ),
  },
  UpdateContactField: {
    label: 'Update Contact Field',
    icon: '✏️',
    inputs: 1,
    outputs: 1,
    data: { field: 'FirstName', value: '' },
    html: node(
      'Update Contact Field',
      '✏️',
      `<select df-field class="${FIELD_CLASS}">
         <option value="FirstName">First name</option>
         <option value="LastName">Last name</option>
         <option value="Company">Company</option>
         <option value="Email">Email</option>
         <option value="Description">Notes</option>
         <option value="City">City</option>
         <option value="State">State</option>
       </select>
       <input df-value placeholder="New value" class="${FIELD_CLASS}" />`
    ),
  },
  SendWebhook: {
    label: 'Call Webhook',
    icon: '🌐',
    inputs: 1,
    outputs: 1,
    data: { url: '', bodytemplate: '' },
    html: node(
      'Call Webhook',
      '🌐',
      `<input df-url placeholder="https://your-endpoint.example.com" class="${FIELD_CLASS}" />
       <textarea df-bodytemplate rows="2" placeholder="Optional JSON body template" class="${FIELD_CLASS} font-mono"></textarea>`
    ),
  },
  Stop: {
    label: 'Stop',
    icon: '⏹',
    inputs: 1,
    outputs: 0,
    data: {},
    html: node('Stop', '⏹', `<p class="text-[11px] text-gray-400">Ends this branch here.</p>`),
  },
};

function escapeHtml(str) {
  const div = document.createElement('div');
  div.textContent = str ?? '';
  return div.innerHTML;
}

// Matches TemplateFormViewModel.MaxBodyVars on the server — WhatsApp templates in this app never
// have more than this many {{n}} body variables.
const MAX_BODY_PARAMS = 6;

/** @param {{templateId: string, name: string, headerFormat: string|null, headerParamsCount: number, bodyParamsCount: number, bodyText: string, language: string}[]} templateOptions */
function sendTemplateHtml(templateOptions) {
  const options = (templateOptions || [])
    .map((t) => `<option value="${escapeHtml(t.name)}">${escapeHtml(t.name)}</option>`)
    .join('');

  const bodyParamInputs = Array.from(
    { length: MAX_BODY_PARAMS },
    (_, i) => `<input df-bodyparam${i + 1} placeholder="Value for {{${i + 1}}}" class="${FIELD_CLASS}" style="display:none" />`
  ).join('');

  return node(
    'Send Template',
    '📄',
    `<select df-templatename class="${FIELD_CLASS}">
       <option value="">Choose a template…</option>
       ${options}
     </select>
     <input df-headerparam placeholder="Header value ({{1}})" class="${FIELD_CLASS}" style="display:none" />
     ${bodyParamInputs}
     ${personalizationChipsHtml()}
     <div class="df-template-preview mt-1 rounded-md p-2" style="background-color:#e5ddd5; display:none"></div>
     <input type="hidden" df-language />`
  );
}

/** Meta serializes template buttons differently depending on origin — PascalCase for templates
 * this app created locally (JsonSerializer.Serialize with no options), whatever casing the Graph
 * API itself uses for templates pulled in via template sync — so this reads both instead of
 * assuming one. Malformed/unrecognized JSON just yields no buttons rather than throwing, since a
 * missing button row is a much smaller problem than breaking the whole preview over it. */
function parseTemplateButtons(buttonsJson) {
  if (!buttonsJson) return [];
  try {
    const raw = JSON.parse(buttonsJson);
    if (!Array.isArray(raw)) return [];
    return raw
      .map((b) => ({ type: b.type || b.Type || '', text: b.text || b.Text || '' }))
      .filter((b) => b.type || b.text);
  } catch {
    return [];
  }
}

/** Builds the same header/body/footer/buttons WhatsApp-bubble mockup as the template editor's own
 * live preview (Templates/Save.cshtml) — so a template picked here shows what will actually land
 * in the customer's chat, not just its raw body text. */
function renderTemplatePreviewHtml(template, headerValue, bodyValues) {
  const parts = [];

  if (template.headerFormat === 'TEXT' && template.headerText) {
    const headerText = template.headerText.replace(/\{\{1\}\}/g, headerValue || '{{1}}');
    parts.push(`<p class="font-semibold text-[12px] text-gray-900 mb-1">${escapeHtml(headerText)}</p>`);
  } else if (template.headerFormat === 'IMAGE') {
    parts.push(
      template.headerMediaUrl
        ? `<img src="${escapeHtml(template.headerMediaUrl)}" class="w-full max-h-28 object-cover rounded mb-1.5" />`
        : `<div class="w-full h-20 bg-gray-200 rounded flex items-center justify-center text-gray-400 text-[10px] mb-1.5">Image header</div>`
    );
  } else if (template.headerFormat === 'VIDEO') {
    parts.push(`<div class="w-full h-20 bg-gray-800 rounded flex items-center justify-center text-white text-[10px] mb-1.5">🎥 Video header</div>`);
  } else if (template.headerFormat === 'DOCUMENT') {
    parts.push(`<div class="w-full py-3 bg-gray-100 rounded flex items-center justify-center text-gray-500 text-[10px] mb-1.5">📄 Document header</div>`);
  }

  let bodyText = template.bodyText || '';
  for (let n = 1; n <= template.bodyParamsCount; n++) {
    bodyText = bodyText.replaceAll(`{{${n}}}`, bodyValues[n - 1] || `{{${n}}}`);
  }
  parts.push(`<p class="text-[12px] text-gray-900 whitespace-pre-wrap">${escapeHtml(bodyText || '(this template has no body text)')}</p>`);

  if (template.footerText) {
    parts.push(`<p class="text-[10px] text-gray-500 mt-1">${escapeHtml(template.footerText)}</p>`);
  }

  const buttonsHtml = parseTemplateButtons(template.buttonsJson)
    .map((b) => `<div class="border-t border-gray-100 px-2 py-1.5 text-center text-[11px] text-blue-600">${escapeHtml(b.text || b.type)}</div>`)
    .join('');

  return `<div class="bg-white rounded-md shadow-sm p-2">${parts.join('')}</div>${buttonsHtml}`;
}

/**
 * Wires up the reactive bits of every SendTemplate node currently (and later) on the canvas —
 * picking a template shows exactly as many body/header param fields as it actually needs, drives
 * the read-only language field from it (a template's language isn't independently choosable), and
 * keeps a live "here's roughly what gets sent" preview in sync as the admin fills in values.
 * Event delegation on the canvas container (rather than per-node listeners) means this needs no
 * special-casing for nodes added later via the palette or reconstructed from a saved automation —
 * same pattern already used for the container's own dragover/drop handling below.
 */
function wireSendTemplateFields(editor, container, templateOptions) {
  const byName = new Map((templateOptions || []).map((t) => [t.name, t]));

  function updateCard(card) {
    if (!card) return;
    const select = card.querySelector('[df-templatename]');
    if (!select) return;
    const template = byName.get(select.value);

    const headerInput = card.querySelector('[df-headerparam]');
    if (headerInput) {
      const showHeader = !!template && template.headerFormat === 'TEXT' && template.headerParamsCount > 0;
      headerInput.style.display = showHeader ? '' : 'none';
    }

    for (let n = 1; n <= MAX_BODY_PARAMS; n++) {
      const input = card.querySelector(`[df-bodyparam${n}]`);
      if (input) input.style.display = template && n <= template.bodyParamsCount ? '' : 'none';
    }

    const languageInput = card.querySelector('[df-language]');
    if (languageInput) languageInput.value = template?.language || 'en_US';

    const preview = card.querySelector('.df-template-preview');
    if (!preview) return;
    if (!template) {
      preview.style.display = 'none';
      preview.innerHTML = '';
      return;
    }

    const bodyValues = [];
    for (let n = 1; n <= template.bodyParamsCount; n++) {
      bodyValues.push(card.querySelector(`[df-bodyparam${n}]`)?.value?.trim() || '');
    }
    const headerValue = card.querySelector('[df-headerparam]')?.value?.trim() || '';

    preview.innerHTML = renderTemplatePreviewHtml(template, headerValue, bodyValues);
    preview.style.display = '';
  }

  container.addEventListener('input', (e) => {
    if (e.target.matches('[df-headerparam], [df-bodyparam1], [df-bodyparam2], [df-bodyparam3], [df-bodyparam4], [df-bodyparam5], [df-bodyparam6]')) {
      updateCard(e.target.closest('.df-node-card'));
    }
  });

  container.addEventListener('change', (e) => {
    if (e.target.matches('[df-templatename]')) {
      updateCard(e.target.closest('.df-node-card'));
    }
  });

  // Covers both a fresh palette-add and canvas reconstruction from a saved automation (where
  // flatConfig has already pre-filled templatename by the time this fires).
  editor.on('nodeCreated', (id) => {
    updateCard(document.querySelector(`#node-${id} .df-node-card`));
  });
}

// Which of Operand/Value each Condition subject actually reads server-side (see
// AutomationEngine.EvaluateConditionAsync) — showing a field the subject ignores, or leaving a
// generic "Field name / HH:mm-HH:mm" placeholder regardless of what's selected, is exactly the
// "overloaded and under-explained" friction a non-technical admin hits here.
const CONDITION_SUBJECT_HELP = {
  MessageContent: { showOperand: false, showValue: true, valuePlaceholder: 'Text to look for (e.g. price)' },
  ContactField: {
    showOperand: true, showValue: true,
    operandPlaceholder: 'Field name', operandHint: 'One of: FirstName, LastName, Company, Email, City, State, Type',
    valuePlaceholder: 'Value to match',
  },
  TimeOfDay: {
    showOperand: true, showValue: false,
    operandPlaceholder: 'e.g. 09:00-18:00', operandHint: '24-hour time range (start-end)',
  },
  TemplateClicked: { showOperand: false, showValue: false },
};

function wireConditionFields(container) {
  function updateCard(card) {
    if (!card) return;
    const subject = card.querySelector('[df-subject]');
    if (!subject) return;
    const help = CONDITION_SUBJECT_HELP[subject.value] || CONDITION_SUBJECT_HELP.MessageContent;

    const operand = card.querySelector('.df-condition-operand');
    const operandHint = card.querySelector('.df-condition-operand-hint');
    const value = card.querySelector('.df-condition-value');

    if (operand) {
      operand.style.display = help.showOperand ? '' : 'none';
      if (help.operandPlaceholder) operand.placeholder = help.operandPlaceholder;
    }
    if (operandHint) {
      operandHint.style.display = help.showOperand && help.operandHint ? '' : 'none';
      operandHint.textContent = help.operandHint || '';
    }
    if (value) {
      value.style.display = help.showValue ? '' : 'none';
      if (help.valuePlaceholder) value.placeholder = help.valuePlaceholder;
    }
  }

  container.addEventListener('change', (e) => {
    if (e.target.matches('[df-subject]')) updateCard(e.target.closest('.df-node-card'));
  });
  container.addEventListener('input', (e) => {
    if (e.target.matches('[df-subject]')) updateCard(e.target.closest('.df-node-card'));
  });

  // Covers both a fresh palette-add and canvas reconstruction from a saved automation.
  return (nodeId) => updateCard(document.querySelector(`#node-${nodeId} .df-node-card`));
}

/**
 * Clicking a "+ First name" chip inserts {{contact.firstName}} into whichever text field the
 * admin last focused within that same node card (falling back to the card's first text field if
 * none was focused yet) — at the cursor position, not just appended, so it can be dropped mid-
 * sentence ("Thank you {{contact.firstName}} for submitting").
 */
function wirePersonalizationChips(container) {
  container.addEventListener(
    'focusin',
    (e) => {
      if (e.target.matches('textarea, input[type="text"], input:not([type])')) {
        const card = e.target.closest('.df-node-card');
        if (card) card._lastFocusedField = e.target;
      }
    },
    true
  );

  container.addEventListener('click', (e) => {
    const chip = e.target.closest('.df-chip');
    if (!chip) return;

    const card = chip.closest('.df-node-card');
    // Fall back to the first VISIBLE text field, not just the first in DOM order — a SendTemplate
    // card's header/body param inputs are individually hidden until a template with that many
    // variables is picked (see wireSendTemplateFields), so a naive first-match could silently
    // target a display:none field the admin can't even see the token land in.
    const visibleFields = Array.from(card?.querySelectorAll('textarea, input[type="text"], input:not([type])') || [])
      .filter((el) => el.offsetParent !== null);
    const field = card?._lastFocusedField && card.contains(card._lastFocusedField) && card._lastFocusedField.offsetParent !== null
      ? card._lastFocusedField
      : visibleFields[0];
    if (!field) return;

    const token = chip.dataset.token;
    const start = field.selectionStart ?? field.value.length;
    const end = field.selectionEnd ?? field.value.length;
    field.value = field.value.slice(0, start) + token + field.value.slice(end);
    field.focus();
    const cursor = start + token.length;
    field.setSelectionRange(cursor, cursor);
    field.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

/** @param {{id: string, name: string}[]} leadForms */
const TRIGGER_HTML = (leadForms) => {
  const formOptions = (leadForms || [])
    .map((f) => `<option value="${escapeHtml(f.id)}">${escapeHtml(f.name)}</option>`)
    .join('');

  return `
  <div class="df-node-card df-trigger-card">
    <div class="df-node-title">⚡ Trigger</div>
    <div class="df-node-body">
      <select df-triggertype class="${FIELD_CLASS}">
        <option value="InboundMessage">Any inbound message</option>
        <option value="FirstInboundMessage">First message from a contact</option>
        <option value="KeywordMatch">Message contains a keyword</option>
        <option value="NewContactCreated">New contact created (any source)</option>
        <option value="FacebookLeadReceived">New Facebook lead received</option>
        <option value="InteractiveReply">Button / list reply tapped</option>
      </select>
      <input df-keywords placeholder="Keywords, comma-separated" class="${FIELD_CLASS}" />
      <select df-matchtype class="${FIELD_CLASS}">
        <option value="contains">Contains</option>
        <option value="exact">Exact match</option>
        <option value="word">Whole word</option>
      </select>
      <select df-casesensitive class="${FIELD_CLASS}">
        <option value="false">Case-insensitive</option>
        <option value="true">Case-sensitive</option>
      </select>
      <input df-replyids placeholder="Button/row ids, comma-separated" class="${FIELD_CLASS}" />
      <select df-leadformid class="${FIELD_CLASS}" title="Only for 'New Facebook lead received' — leave as Any to match every form on the connected Page">
        <option value="">Any Facebook Lead Ads form</option>
        ${formOptions}
      </select>
    </div>
  </div>`;
};

// WhatsApp only allows a *template* message to someone with no open 24-hour session — exactly
// the state of a brand-new Lead Ads contact or any other freshly-created contact who hasn't
// messaged in. SendButtons/SendMessage sent as the very first step off one of these triggers
// will be silently rejected by Meta's own API; this just warns in the builder before that
// happens, rather than leaving it to be discovered later in the run Logs.
const NO_SESSION_TRIGGERS = new Set(['FacebookLeadReceived', 'NewContactCreated']);
const SESSION_RESTRICTED_STEPS = new Set(['SendButtons', 'SendMessage']);

function computeSessionWindowWarning(editor) {
  const exported = editor.export();
  const nodesById = exported.drawflow.Home.data;
  const triggerEntry = Object.values(nodesById).find((n) => n.name === 'Trigger');
  if (!triggerEntry) return null;

  const triggerType = triggerEntry.data?.triggertype;
  if (!NO_SESSION_TRIGGERS.has(triggerType)) return null;

  const firstConn = triggerEntry.outputs?.output_1?.connections?.[0];
  if (!firstConn) return null;
  const firstStep = nodesById[firstConn.node];
  if (!firstStep || !SESSION_RESTRICTED_STEPS.has(firstStep.name)) return null;

  const triggerLabel = triggerType === 'FacebookLeadReceived' ? 'a new Facebook lead' : 'a new contact';
  const stepLabel = firstStep.name === 'SendButtons' ? '"Send Buttons"' : '"Send Message"';
  return `${stepLabel} can't reach ${triggerLabel} as the first step — WhatsApp only allows a Template message to someone who hasn't messaged you yet. Use "Send Template" first instead.`;
}

export function createAutomationCanvas(container, initial, onGraphChange) {
  const editor = new Drawflow(container);
  editor.reroute = true;
  editor.zoom_max = 1.4;
  editor.zoom_min = 0.4;
  editor.start();

  const leadForms = initial?.leadForms || [];
  const templateOptions = initial?.templateOptions || [];

  function addStepNode(type, x, y, data) {
    const def = STEP_DEFS[type];
    if (!def) return null;
    const merged = { ...def.data, ...(data || {}) };
    const html = type === 'SendTemplate' ? sendTemplateHtml(templateOptions) : def.html;
    const id = editor.addNode(type, def.inputs, def.outputs, x, y, `df-step ${type}`, merged, html, false);
    return id;
  }

  wireSendTemplateFields(editor, container, templateOptions);
  wirePersonalizationChips(container);
  const updateConditionCard = wireConditionFields(container);
  editor.on('nodeCreated', updateConditionCard);

  function addTriggerNode(x, y, triggerType, data) {
    const merged = { triggertype: triggerType || 'InboundMessage', keywords: '', matchtype: 'contains', casesensitive: 'false', replyids: '', leadformid: '', ...(data || {}) };
    return editor.addNode('Trigger', 0, 1, x, y, 'df-step df-trigger', merged, TRIGGER_HTML(leadForms), false);
  }

  // ---- Initial layout: either a fresh trigger-only canvas, or reconstruct from a saved tree ----
  let triggerNodeId;
  if (!initial || !initial.steps || initial.steps.length === 0) {
    triggerNodeId = addTriggerNode(50, 200, initial?.triggerType, initial?.triggerFields);
  } else {
    triggerNodeId = addTriggerNode(50, 200, initial.triggerType, initial.triggerFields);
    layoutChain(initial.steps, 420, 200, triggerNodeId, 'output_1');
  }

  // ---- Session-window risk warning: recompute on anything that could change the answer ----
  function notifyGraphChanged() {
    if (typeof onGraphChange === 'function') {
      onGraphChange({ sessionWindowWarning: computeSessionWindowWarning(editor) });
    }
  }

  editor.on('connectionCreated', notifyGraphChanged);
  editor.on('connectionRemoved', notifyGraphChanged);
  editor.on('nodeCreated', notifyGraphChanged);
  editor.on('nodeRemoved', notifyGraphChanged);
  container.addEventListener('change', (e) => {
    if (e.target.matches('[df-triggertype]')) notifyGraphChanged();
  });
  notifyGraphChanged();

  // A canvas Condition node only ever has two outputs (Yes/No) — there's no third "runs
  // regardless" edge, matching standard flowchart notation. So a chain stops being laid out
  // once it hits a Condition; anything the old list-based builder might have placed *after* a
  // Condition in the same flat array (a shape only that UI could produce) has no canvas
  // equivalent and is intentionally not reconstructed here.
  function layoutChain(steps, x, y, fromNodeId, fromOutput) {
    let prevId = fromNodeId;
    let prevOutput = fromOutput;
    let curY = y;
    for (const step of steps) {
      const id = addStepNode(step.type, x, curY, flatConfig(step.type, step.config));
      editor.addConnection(prevId, id, prevOutput, 'input_1');
      if (step.type === 'Condition') {
        if (step.yes?.length) layoutChain(step.yes, x + 380, curY - 60, id, 'output_1');
        if (step.no?.length) layoutChain(step.no, x + 380, curY + 220, id, 'output_2');
        return;
      }

      prevId = id;
      prevOutput = 'output_1';
      x += 380;
    }
  }

  /** Server config JSON (camelCase) -> flat all-lowercase df-* field shape. */
  function flatConfig(type, config) {
    if (!config) return {};
    if (type === 'SendButtons') {
      const buttons = config.buttons || [];
      const flat = { bodytext: config.bodyText || '' };
      [0, 1, 2].forEach((i) => {
        flat[`button${i + 1}id`] = buttons[i]?.id || '';
        flat[`button${i + 1}title`] = buttons[i]?.title || '';
      });
      return flat;
    }
    if (type === 'SendTemplate') {
      const bodyParams = config.bodyParams || [];
      const flat = {
        templatename: config.templateName || '',
        language: config.language || 'en_US',
        headerparam: config.headerParam || '',
      };
      for (let i = 0; i < MAX_BODY_PARAMS; i++) {
        flat[`bodyparam${i + 1}`] = bodyParams[i] || '';
      }
      return flat;
    }
    if (type === 'SendWebhook') {
      return { url: config.url || '', bodytemplate: config.bodyTemplate || '' };
    }
    return { ...config };
  }

  /** Inverse of flatConfig, for export: all-lowercase canvas fields -> server's camelCase config
   * shape. Also fixes up types df-* binding always hands back as strings (a number input's
   * .value is a string like any other) but the server's C# config classes expect as real
   * numbers/booleans — a JSON string where an int is expected throws on deserialization rather
   * than silently coercing. */
  function unflatConfig(type, data) {
    if (type === 'SendButtons') {
      return {
        bodyText: data.bodytext || '',
        buttons: [1, 2, 3]
          .map((i) => ({ id: data[`button${i}id`] || '', title: data[`button${i}title`] || '' }))
          .filter((b) => b.id && b.title),
      };
    }

    if (type === 'Wait') {
      return { amount: Math.max(1, Number(data.amount) || 1), unit: data.unit || 'minutes' };
    }

    if (type === 'SendTemplate') {
      // Every slot up to MAX_BODY_PARAMS is always present in `data` (Drawflow binds whatever
      // exists in the node's HTML regardless of current visibility), so this slices down to only
      // the params the *currently selected* template actually needs — otherwise switching from a
      // 3-variable template to a 1-variable one would still export the two stale hidden values.
      const template = (templateOptions || []).find((t) => t.name === data.templatename);
      const bodyCount = template?.bodyParamsCount ?? 0;
      const bodyParams = [];
      for (let i = 1; i <= bodyCount; i++) {
        bodyParams.push(data[`bodyparam${i}`] || '');
      }

      const hasTextHeader = template?.headerFormat === 'TEXT' && template.headerParamsCount > 0;

      return {
        templateName: data.templatename || '',
        language: data.language || 'en_US',
        headerParam: hasTextHeader ? data.headerparam || '' : null,
        bodyParams,
      };
    }

    if (type === 'SendWebhook') {
      return { url: data.url || '', bodyTemplate: data.bodytemplate || '' };
    }

    return { ...data };
  }

  // ---- Palette: click-to-add at a sensible open spot, or drag-and-drop onto the canvas ----
  // Successive click-adds cascade by a small offset so new nodes don't stack exactly on top of
  // each other (there's no natural "drop position" for a click, unlike an actual drag).
  let clickAddCount = 0;
  function nextOpenSpot() {
    const rect = container.getBoundingClientRect();
    const cascade = (clickAddCount++ % 6) * 30;
    return {
      x: (rect.width / 2) * (1 / editor.zoom) - editor.canvas_x * (1 / editor.zoom) - 100 + cascade,
      y: (rect.height / 2) * (1 / editor.zoom) - editor.canvas_y * (1 / editor.zoom) - 40 + cascade,
    };
  }

  container.addEventListener('dragover', (e) => e.preventDefault());
  container.addEventListener('drop', (e) => {
    e.preventDefault();
    const type = e.dataTransfer.getData('text/step-type');
    if (!type || !STEP_DEFS[type]) return;
    const precanvasRect = editor.precanvas.getBoundingClientRect();
    const x = e.clientX * (1 / editor.zoom) - precanvasRect.x * (1 / editor.zoom);
    const y = e.clientY * (1 / editor.zoom) - precanvasRect.y * (1 / editor.zoom);
    addStepNode(type, x, y);
  });

  // ---- Export: walk the graph from the Trigger node, following connections, back into the tree JSON the server expects ----

  /** Walks starting from a specific output port of nodeId (root chain from the trigger, or a Condition's Yes/No branch). */
  function walkFrom(nodesById, nodeId, outputPort, visited) {
    if (!outputPort) return [];
    const n = nodesById[nodeId];
    const conn = n?.outputs?.[outputPort]?.connections?.[0];
    if (!conn) return [];
    const next = nodesById[conn.node];
    if (!next || visited.has(String(next.id))) return [];
    visited.add(String(next.id));

    const type = next.name;
    const stepNode = { type, config: unflatConfig(type, next.data) };
    if (type === 'Condition') {
      const yesConn = next.outputs?.output_1?.connections?.[0];
      const noConn = next.outputs?.output_2?.connections?.[0];
      stepNode.yes = yesConn ? walkFrom(nodesById, next.id, 'output_1', new Set(visited)) : [];
      stepNode.no = noConn ? walkFrom(nodesById, next.id, 'output_2', new Set(visited)) : [];
      return [stepNode];
    }

    return [stepNode, ...walkFrom(nodesById, next.id, 'output_1', visited)];
  }

  function exportForSubmit() {
    const exported = editor.export();
    const nodesById = exported.drawflow.Home.data;

    const triggerEntry = Object.values(nodesById).find((n) => n.name === 'Trigger');
    const triggerData = triggerEntry?.data || {};
    const stepsTree = triggerEntry ? walkFrom(nodesById, triggerEntry.id, 'output_1', new Set([String(triggerEntry.id)])) : [];

    return {
      triggerType: triggerData.triggertype || 'InboundMessage',
      keywords: triggerData.keywords || '',
      matchType: triggerData.matchtype || 'contains',
      caseSensitive: triggerData.casesensitive === 'true',
      replyIds: triggerData.replyids || '',
      leadFormId: triggerData.leadformid || '',
      stepsJson: JSON.stringify(stepsTree),
    };
  }

  return {
    editor,
    addStepNode(type) {
      const spot = nextOpenSpot();
      addStepNode(type, spot.x, spot.y);
    },
    startDrag(event, type) {
      event.dataTransfer.setData('text/step-type', type);
    },
    exportForSubmit,
    zoomIn: () => editor.zoom_in(),
    zoomOut: () => editor.zoom_out(),
    zoomReset: () => editor.zoom_reset(),
  };
}

window.createAutomationCanvas = createAutomationCanvas;
window.AUTOMATION_STEP_DEFS = Object.fromEntries(
  Object.entries(STEP_DEFS).map(([key, def]) => [key, { label: def.label, icon: def.icon }])
);
