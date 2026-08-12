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
      `<textarea df-text rows="3" placeholder="Hi {{message.text}}..." class="${FIELD_CLASS}"></textarea>`
    ),
  },
  SendTemplate: {
    label: 'Send Template',
    icon: '📄',
    inputs: 1,
    outputs: 1,
    data: { templatename: '', language: 'en_US', bodyparamscsv: '' },
    html: node(
      'Send Template',
      '📄',
      `<input df-templatename placeholder="Template name" class="${FIELD_CLASS}" />
       <input df-language placeholder="Language, e.g. en_US" class="${FIELD_CLASS}" />
       <input df-bodyparamscsv placeholder="Body params, comma-separated" class="${FIELD_CLASS}" />`
    ),
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
       </div>`
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
       </select>
       <input df-operand placeholder="Field name / HH:mm-HH:mm (if needed)" class="${FIELD_CLASS}" />
       <input df-value placeholder="Value to compare" class="${FIELD_CLASS}" />
       <div class="mt-1 flex justify-between text-[10px] font-semibold px-1">
         <span class="text-green-600">YES ↓</span><span class="text-red-600">NO ↓</span>
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

const TRIGGER_HTML = () => `
  <div class="df-node-card df-trigger-card">
    <div class="df-node-title">⚡ Trigger</div>
    <div class="df-node-body">
      <select df-triggertype class="${FIELD_CLASS}">
        <option value="InboundMessage">Any inbound message</option>
        <option value="FirstInboundMessage">First message from a contact</option>
        <option value="KeywordMatch">Message contains a keyword</option>
        <option value="NewContactCreated">New contact created</option>
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
    </div>
  </div>`;

export function createAutomationCanvas(container, initial) {
  const editor = new Drawflow(container);
  editor.reroute = true;
  editor.zoom_max = 1.4;
  editor.zoom_min = 0.4;
  editor.start();

  function addStepNode(type, x, y, data) {
    const def = STEP_DEFS[type];
    if (!def) return null;
    const merged = { ...def.data, ...(data || {}) };
    const id = editor.addNode(type, def.inputs, def.outputs, x, y, `df-step ${type}`, merged, def.html, false);
    return id;
  }

  function addTriggerNode(x, y, triggerType, data) {
    const merged = { triggertype: triggerType || 'InboundMessage', keywords: '', matchtype: 'contains', casesensitive: 'false', replyids: '', ...(data || {}) };
    return editor.addNode('Trigger', 0, 1, x, y, 'df-step df-trigger', merged, TRIGGER_HTML(), false);
  }

  // ---- Initial layout: either a fresh trigger-only canvas, or reconstruct from a saved tree ----
  let triggerNodeId;
  if (!initial || !initial.steps || initial.steps.length === 0) {
    triggerNodeId = addTriggerNode(50, 200, initial?.triggerType, initial?.triggerFields);
  } else {
    triggerNodeId = addTriggerNode(50, 200, initial.triggerType, initial.triggerFields);
    layoutChain(initial.steps, 420, 200, triggerNodeId, 'output_1');
  }

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
      return {
        templatename: config.templateName || '',
        language: config.language || 'en_US',
        bodyparamscsv: (config.bodyParams || []).join(', '),
      };
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
      return {
        templateName: data.templatename || '',
        language: data.language || 'en_US',
        bodyParams: (data.bodyparamscsv || '')
          .split(',')
          .map((s) => s.trim())
          .filter(Boolean),
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
