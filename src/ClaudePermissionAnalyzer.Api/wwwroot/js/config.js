/* ==========================================================================
   Config Page Logic
   ========================================================================== */

let currentConfig = null;
let isDirty = false;

/* Hook handlers local state - edited independently from the config form fields */
let hookHandlersDirty = false;
let promptTemplateNames = [];

const HOOK_EVENT_TYPES = [
    'PermissionRequest',
    'PreToolUse',
    'PostToolUse',
    'PostToolUseFailure',
    'UserPromptSubmit',
    'Stop'
];

const HANDLER_MODES = [
    { value: 'llm-analysis', label: 'LLM Analysis' },
    { value: 'log-only', label: 'Log Only' },
    { value: 'context-injection', label: 'Context Injection' },
    { value: 'custom-logic', label: 'Custom Logic' }
];

const HOOK_EVENT_DESCRIPTIONS = {
    'PermissionRequest': 'Gate for Bash, Read, Write, Web, and MCP operations. Returns allow/deny decisions.',
    'PreToolUse': 'Pre-execution safety gate. Can allow, deny, or ask the user.',
    'PostToolUse': 'Post-execution validation. Can inject additional context.',
    'PostToolUseFailure': 'Handles tool execution failures. Typically log-only.',
    'UserPromptSubmit': 'Fires when a user submits a prompt. Typically log-only.',
    'Stop': 'Fires when a Claude session ends. Used for session cleanup.'
};

async function refreshData() {
    await loadConfig();
}

async function loadConfig() {
    const container = document.getElementById('configContent');
    if (!container) return;

    try {
        currentConfig = await fetchApi('/api/config');
        renderConfig(currentConfig);
        isDirty = false;
        updateSaveButton();
        // Also render hook handlers
        await loadPromptTemplates();
        renderHookHandlers();
        hookHandlersDirty = false;
        updateHookHandlersSaveButton();
    } catch (error) {
        container.innerHTML = `
            <div class="error-state">
                <h3>Failed to load configuration</h3>
                <p>${escapeHtml(error.message)}</p>
                <button class="btn" onclick="loadConfig()">Retry</button>
            </div>
        `;
    }
}

async function loadPromptTemplates() {
    try {
        const templates = await fetchApi('/api/prompts');
        if (templates && typeof templates === 'object') {
            promptTemplateNames = Object.keys(templates);
        } else {
            promptTemplateNames = [];
        }
    } catch (e) {
        promptTemplateNames = [];
    }
}

function renderConfig(config) {
    const container = document.getElementById('configContent');
    if (!container) return;

    container.innerHTML = `
        <div class="config-section">
            <h3>Server</h3>
            <div class="config-field">
                <label class="config-label" for="cfg-host">
                    Host
                    <small>The hostname the server listens on</small>
                </label>
                <input id="cfg-host" class="config-input" type="text"
                    value="${escapeAttr(config.server?.host || 'localhost')}"
                    data-path="server.host" aria-label="Server host">
            </div>
            <div class="config-field">
                <label class="config-label" for="cfg-port">
                    Port
                    <small>TCP port for the HTTP API</small>
                </label>
                <input id="cfg-port" class="config-input" type="number"
                    value="${config.server?.port || 5050}" min="1024" max="65535"
                    data-path="server.port" aria-label="Server port">
            </div>
        </div>

        <div class="config-section">
            <h3>LLM Provider</h3>
            <div class="config-field">
                <label class="config-label" for="cfg-provider">
                    Provider
                    <small>LLM backend used for analysis</small>
                </label>
                <select id="cfg-provider" class="config-input"
                    data-path="llm.provider" aria-label="LLM provider">
                    <option value="claude-cli" ${(config.llm?.provider || 'claude-cli') === 'claude-cli' ? 'selected' : ''}>Claude Code CLI</option>
                    <option value="copilot-cli" ${config.llm?.provider === 'copilot-cli' ? 'selected' : ''}>GitHub Copilot CLI</option>
                </select>
            </div>
            <div class="config-field">
                <label class="config-label" for="cfg-model">
                    Model
                    <small>Model identifier for the LLM</small>
                </label>
                <input id="cfg-model" class="config-input" type="text"
                    value="${escapeAttr(config.llm?.model || 'sonnet')}"
                    data-path="llm.model" aria-label="LLM model">
            </div>
            <div class="config-field">
                <label class="config-label" for="cfg-timeout">
                    Timeout (ms)
                    <small>Maximum wait time for LLM responses</small>
                </label>
                <input id="cfg-timeout" class="config-input" type="number"
                    value="${config.llm?.timeout || 30000}" min="1000" max="300000"
                    data-path="llm.timeout" aria-label="LLM timeout">
            </div>
        </div>

        <div class="config-section">
            <h3>Session</h3>
            <div class="config-field">
                <label class="config-label" for="cfg-maxhistory">
                    Max History Per Session
                    <small>Number of events to retain per session</small>
                </label>
                <input id="cfg-maxhistory" class="config-input" type="number"
                    value="${config.session?.maxHistoryPerSession || 50}" min="1" max="1000"
                    data-path="session.maxHistoryPerSession" aria-label="Max history per session">
            </div>
            <div class="config-field">
                <label class="config-label" for="cfg-storagedir">
                    Storage Directory
                    <small>File path for session storage</small>
                </label>
                <input id="cfg-storagedir" class="config-input" type="text"
                    value="${escapeAttr(config.session?.storageDir || '')}"
                    data-path="session.storageDir" aria-label="Storage directory"
                    style="width: 300px;">
            </div>
        </div>

        <div class="config-actions">
            <button class="btn" onclick="loadConfig()">Reset</button>
            <button class="btn btn-primary" id="saveConfigBtn" onclick="saveConfig()" disabled>Save Changes</button>
        </div>
    `;

    // Add change listeners (use both 'input' and 'change' for <select> compatibility)
    container.querySelectorAll('.config-input').forEach(input => {
        const eventType = input.tagName === 'SELECT' ? 'change' : 'input';
        input.addEventListener(eventType, () => {
            isDirty = true;
            updateSaveButton();
        });
    });
}

function updateSaveButton() {
    const btn = document.getElementById('saveConfigBtn');
    if (btn) {
        btn.disabled = !isDirty;
    }
}

function updateHookHandlersSaveButton() {
    const btn = document.getElementById('saveHookHandlersBtn');
    const indicator = document.getElementById('hookHandlersDirtyIndicator');
    if (btn) {
        btn.disabled = !hookHandlersDirty;
    }
    if (indicator) {
        indicator.style.display = hookHandlersDirty ? 'inline' : 'none';
    }
}

function markHookHandlersDirty() {
    hookHandlersDirty = true;
    updateHookHandlersSaveButton();
}

async function saveConfig() {
    if (!currentConfig || !isDirty) return;

    const inputs = document.querySelectorAll('.config-input');
    const updated = JSON.parse(JSON.stringify(currentConfig));

    inputs.forEach(input => {
        const path = input.dataset.path;
        if (!path) return;

        const parts = path.split('.');
        let obj = updated;
        for (let i = 0; i < parts.length - 1; i++) {
            if (!obj[parts[i]]) obj[parts[i]] = {};
            obj = obj[parts[i]];
        }

        const key = parts[parts.length - 1];
        if (input.type === 'number') {
            obj[key] = parseInt(input.value, 10);
        } else {
            obj[key] = input.value;
        }
    });

    const btn = document.getElementById('saveConfigBtn');
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Saving...';
    }

    try {
        await fetch('/api/config', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(updated)
        });

        currentConfig = updated;
        isDirty = false;
        Toast.show('Configuration Saved', 'Changes have been saved successfully. Some changes may require a restart.', 'success');
    } catch (error) {
        Toast.show('Save Failed', error.message, 'danger');
    } finally {
        if (btn) {
            btn.textContent = 'Save Changes';
            updateSaveButton();
        }
    }
}

/* ==========================================================================
   Hook Handlers UI
   ========================================================================== */

function getHookHandlers() {
    if (!currentConfig) return {};
    return currentConfig.hookHandlers || {};
}

function ensureHookEventConfig(eventType) {
    if (!currentConfig.hookHandlers) {
        currentConfig.hookHandlers = {};
    }
    if (!currentConfig.hookHandlers[eventType]) {
        currentConfig.hookHandlers[eventType] = { enabled: true, handlers: [] };
    }
    return currentConfig.hookHandlers[eventType];
}

function renderHookHandlers() {
    const container = document.getElementById('hookHandlersContent');
    if (!container || !currentConfig) return;

    const hookHandlers = getHookHandlers();

    let html = '';
    for (const eventType of HOOK_EVENT_TYPES) {
        const eventConfig = hookHandlers[eventType] || { enabled: true, handlers: [] };
        const handlers = eventConfig.handlers || [];
        const enabled = eventConfig.enabled !== false;
        const description = HOOK_EVENT_DESCRIPTIONS[eventType] || '';

        html += `
        <div class="hook-event-card" data-event="${escapeAttr(eventType)}" style="
            border: 1px solid var(--border-color);
            border-radius: var(--radius-md);
            padding: 16px;
            margin-bottom: 12px;
            background: var(--bg-tertiary);
        ">
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                <div style="display: flex; align-items: center; gap: 10px;">
                    <h4 style="font-size: 14px; font-weight: 600; color: var(--text-primary); margin: 0;">${escapeHtml(eventType)}</h4>
                    <label style="display: flex; align-items: center; gap: 4px; font-size: 12px; color: var(--text-muted); cursor: pointer;">
                        <input type="checkbox" ${enabled ? 'checked' : ''}
                            onchange="toggleHookEvent('${escapeAttr(eventType)}', this.checked)"
                            style="cursor: pointer;">
                        Enabled
                    </label>
                </div>
                <button class="btn btn-sm" onclick="addHandler('${escapeAttr(eventType)}')" style="font-size: 11px;">+ Add Handler</button>
            </div>
            <p style="font-size: 11px; color: var(--text-faint); margin-bottom: 12px;">${escapeHtml(description)}</p>
            <div id="handlers-${escapeAttr(eventType)}">
                ${handlers.length === 0
                    ? '<p style="font-size: 12px; color: var(--text-faint); font-style: italic; padding: 8px 0;">No handlers configured. Click "+ Add Handler" to create one.</p>'
                    : handlers.map((h, idx) => renderHandlerRow(eventType, h, idx, false)).join('')
                }
            </div>
        </div>`;
    }

    container.innerHTML = html;
}

function renderHandlerRow(eventType, handler, index, editing) {
    const safeEvent = escapeAttr(eventType);
    const rowId = `handler-${safeEvent}-${index}`;

    if (editing) {
        return renderHandlerEditRow(eventType, handler, index);
    }

    const modeBadgeColor = {
        'llm-analysis': 'var(--color-info)',
        'log-only': 'var(--text-faint)',
        'context-injection': 'var(--color-warning)',
        'custom-logic': 'var(--color-success)'
    };
    const badgeColor = modeBadgeColor[handler.mode] || 'var(--text-faint)';

    return `
    <div id="${rowId}" style="
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 8px 10px;
        border: 1px solid var(--border-light);
        border-radius: var(--radius-sm);
        margin-bottom: 6px;
        background: var(--bg-secondary);
        font-size: 12px;
    ">
        <div style="flex: 1; display: flex; align-items: center; gap: 12px; flex-wrap: wrap; min-width: 0;">
            <span style="font-weight: 600; color: var(--text-primary); min-width: 80px;" title="Handler name">${escapeHtml(handler.name || '(unnamed)')}</span>
            <span style="
                display: inline-block;
                padding: 2px 8px;
                border-radius: 10px;
                background: ${badgeColor};
                color: #fff;
                font-size: 10px;
                font-weight: 500;
                white-space: nowrap;
            " title="Mode">${escapeHtml(handler.mode || 'log-only')}</span>
            <code style="
                font-family: var(--font-mono);
                font-size: 11px;
                color: var(--text-muted);
                background: var(--bg-tertiary);
                padding: 1px 6px;
                border-radius: 3px;
                max-width: 200px;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            " title="Matcher pattern: ${escapeAttr(handler.matcher || '*')}">${escapeHtml(handler.matcher || '*')}</code>
            <span style="color: var(--text-faint); white-space: nowrap; font-size: 0.9em;" title="Thresholds: Strict / Moderate / Permissive">S:<strong style="color: var(--text-secondary);">${handler.thresholdStrict || 95}</strong> M:<strong style="color: var(--text-secondary);">${handler.thresholdModerate || 85}</strong> P:<strong style="color: var(--text-secondary);">${handler.thresholdPermissive || 70}</strong></span>
            ${handler.autoApprove ? '<span style="color: var(--color-success); white-space: nowrap;" title="Auto-approve enabled">Auto-approve</span>' : ''}
            ${handler.promptTemplate ? `<span style="color: var(--text-faint); white-space: nowrap; max-width: 150px; overflow: hidden; text-overflow: ellipsis;" title="Prompt: ${escapeAttr(handler.promptTemplate)}">Prompt: ${escapeHtml(handler.promptTemplate.replace(/^.*[\\\/]/, ''))}</span>` : ''}
        </div>
        <div style="display: flex; gap: 4px; flex-shrink: 0;">
            <button class="btn btn-sm" onclick="editHandler('${safeEvent}', ${index})" style="font-size: 11px; padding: 2px 8px;">Edit</button>
            <button class="btn btn-sm" onclick="removeHandler('${safeEvent}', ${index})" style="font-size: 11px; padding: 2px 8px; color: var(--color-danger); border-color: var(--color-danger);">Remove</button>
        </div>
    </div>`;
}

function renderHandlerEditRow(eventType, handler, index) {
    const safeEvent = escapeAttr(eventType);
    const rowId = `handler-${safeEvent}-${index}`;

    // Match by filename - handler.promptTemplate may be a full path
    const currentPromptFile = handler.promptTemplate ? handler.promptTemplate.replace(/^.*[\\\/]/, '') : '';
    const promptOptions = promptTemplateNames.map(name =>
        `<option value="${escapeAttr(name)}" ${currentPromptFile === name ? 'selected' : ''}>${escapeHtml(name)}</option>`
    ).join('');

    const modeOptions = HANDLER_MODES.map(m =>
        `<option value="${escapeAttr(m.value)}" ${handler.mode === m.value ? 'selected' : ''}>${escapeHtml(m.label)}</option>`
    ).join('');

    return `
    <div id="${rowId}" style="
        padding: 12px;
        border: 2px solid var(--accent-primary);
        border-radius: var(--radius-sm);
        margin-bottom: 6px;
        background: var(--bg-secondary);
        font-size: 12px;
    ">
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-bottom: 10px;">
            <div>
                <label style="display: block; font-size: 11px; color: var(--text-faint); margin-bottom: 2px; font-weight: 500;">Name</label>
                <input type="text" id="${rowId}-name" value="${escapeAttr(handler.name || '')}"
                    placeholder="e.g. bash-analyzer"
                    style="width: 100%; padding: 4px 8px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); background: var(--bg-tertiary); color: var(--text-secondary); font-size: 12px; font-family: var(--font-mono);">
            </div>
            <div>
                <label style="display: block; font-size: 11px; color: var(--text-faint); margin-bottom: 2px; font-weight: 500;">Matcher Pattern (regex)</label>
                <input type="text" id="${rowId}-matcher" value="${escapeAttr(handler.matcher || '')}"
                    placeholder="e.g. Bash|Write or *"
                    style="width: 100%; padding: 4px 8px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); background: var(--bg-tertiary); color: var(--text-secondary); font-size: 12px; font-family: var(--font-mono);">
            </div>
            <div>
                <label style="display: block; font-size: 11px; color: var(--text-faint); margin-bottom: 2px; font-weight: 500;">Mode</label>
                <select id="${rowId}-mode"
                    style="width: 100%; padding: 4px 8px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); background: var(--bg-tertiary); color: var(--text-secondary); font-size: 12px;">
                    ${modeOptions}
                </select>
            </div>
            <div>
                <label style="display: block; font-size: 11px; color: var(--text-faint); margin-bottom: 2px; font-weight: 500;">Prompt Template</label>
                <select id="${rowId}-prompt"
                    style="width: 100%; padding: 4px 8px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); background: var(--bg-tertiary); color: var(--text-secondary); font-size: 12px;">
                    <option value="">(none)</option>
                    ${promptOptions}
                </select>
            </div>
            <div>
                <label style="display: block; font-size: 11px; color: var(--text-faint); margin-bottom: 2px; font-weight: 500;">Strict Threshold</label>
                <input type="number" id="${rowId}-thresholdStrict" value="${handler.thresholdStrict != null ? handler.thresholdStrict : 95}"
                    min="0" max="100" style="width: 100%; padding: 4px 8px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); background: var(--bg-tertiary); color: var(--text-secondary); font-size: 12px; font-family: var(--font-mono);">
            </div>
            <div>
                <label style="display: block; font-size: 11px; color: var(--text-faint); margin-bottom: 2px; font-weight: 500;">Moderate Threshold</label>
                <input type="number" id="${rowId}-thresholdModerate" value="${handler.thresholdModerate != null ? handler.thresholdModerate : 85}"
                    min="0" max="100" style="width: 100%; padding: 4px 8px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); background: var(--bg-tertiary); color: var(--text-secondary); font-size: 12px; font-family: var(--font-mono);">
            </div>
            <div>
                <label style="display: block; font-size: 11px; color: var(--text-faint); margin-bottom: 2px; font-weight: 500;">Permissive Threshold</label>
                <input type="number" id="${rowId}-thresholdPermissive" value="${handler.thresholdPermissive != null ? handler.thresholdPermissive : 70}"
                    min="0" max="100" style="width: 100%; padding: 4px 8px; border: 1px solid var(--border-color); border-radius: var(--radius-sm); background: var(--bg-tertiary); color: var(--text-secondary); font-size: 12px; font-family: var(--font-mono);">
            </div>
            <div style="display: flex; align-items: flex-end;">
                <label style="display: flex; align-items: center; gap: 6px; font-size: 12px; color: var(--text-secondary); cursor: pointer; padding-bottom: 4px;">
                    <input type="checkbox" id="${rowId}-autoapprove" ${handler.autoApprove ? 'checked' : ''}
                        style="cursor: pointer;">
                    Auto-approve when safe
                </label>
            </div>
        </div>
        <div style="display: flex; gap: 6px; justify-content: flex-end;">
            <button class="btn btn-sm" onclick="cancelEditHandler('${safeEvent}', ${index})" style="font-size: 11px; padding: 3px 10px;">Cancel</button>
            <button class="btn btn-sm btn-primary" onclick="applyEditHandler('${safeEvent}', ${index})" style="font-size: 11px; padding: 3px 10px;">Apply</button>
        </div>
    </div>`;
}

function toggleHookEvent(eventType, enabled) {
    const eventConfig = ensureHookEventConfig(eventType);
    eventConfig.enabled = enabled;
    markHookHandlersDirty();
}

function addHandler(eventType) {
    const eventConfig = ensureHookEventConfig(eventType);
    const newHandler = {
        name: '',
        matcher: '*',
        mode: 'log-only',
        promptTemplate: '',
        threshold: 85,
        autoApprove: false
    };
    eventConfig.handlers.push(newHandler);
    markHookHandlersDirty();

    // Re-render the handlers list for this event, with the new one in edit mode
    const handlersContainer = document.getElementById(`handlers-${eventType}`);
    if (handlersContainer) {
        const handlers = eventConfig.handlers;
        handlersContainer.innerHTML = handlers.map((h, idx) => {
            if (idx === handlers.length - 1) {
                return renderHandlerRow(eventType, h, idx, true);
            }
            return renderHandlerRow(eventType, h, idx, false);
        }).join('');
    }
}

function removeHandler(eventType, index) {
    const eventConfig = ensureHookEventConfig(eventType);
    if (index >= 0 && index < eventConfig.handlers.length) {
        const name = eventConfig.handlers[index].name || '(unnamed)';
        if (!confirm(`Remove handler "${name}" from ${eventType}?`)) return;
        eventConfig.handlers.splice(index, 1);
        markHookHandlersDirty();
        // Re-render this event's handlers
        renderEventHandlers(eventType);
    }
}

function editHandler(eventType, index) {
    const eventConfig = ensureHookEventConfig(eventType);
    const handler = eventConfig.handlers[index];
    if (!handler) return;

    const handlersContainer = document.getElementById(`handlers-${eventType}`);
    if (handlersContainer) {
        const handlers = eventConfig.handlers;
        handlersContainer.innerHTML = handlers.map((h, idx) => {
            return renderHandlerRow(eventType, h, idx, idx === index);
        }).join('');
    }
}

function cancelEditHandler(eventType, index) {
    renderEventHandlers(eventType);
}

function applyEditHandler(eventType, index) {
    const safeEvent = escapeAttr(eventType);
    const rowId = `handler-${safeEvent}-${index}`;

    const nameEl = document.getElementById(`${rowId}-name`);
    const matcherEl = document.getElementById(`${rowId}-matcher`);
    const modeEl = document.getElementById(`${rowId}-mode`);
    const promptEl = document.getElementById(`${rowId}-prompt`);
    const thresholdStrictEl = document.getElementById(`${rowId}-thresholdStrict`);
    const thresholdModerateEl = document.getElementById(`${rowId}-thresholdModerate`);
    const thresholdPermissiveEl = document.getElementById(`${rowId}-thresholdPermissive`);
    const autoApproveEl = document.getElementById(`${rowId}-autoapprove`);

    if (!nameEl) return;

    const eventConfig = ensureHookEventConfig(eventType);
    const handler = eventConfig.handlers[index];
    if (!handler) return;

    handler.name = nameEl.value.trim();
    handler.matcher = matcherEl.value.trim() || '*';
    handler.mode = modeEl.value;
    handler.promptTemplate = promptEl.value || null;
    handler.thresholdStrict = parseInt(thresholdStrictEl?.value, 10) || 95;
    handler.thresholdModerate = parseInt(thresholdModerateEl?.value, 10) || 85;
    handler.thresholdPermissive = parseInt(thresholdPermissiveEl?.value, 10) || 70;
    handler.threshold = handler.thresholdModerate; // Default threshold = moderate
    handler.autoApprove = autoApproveEl.checked;

    markHookHandlersDirty();
    renderEventHandlers(eventType);
}

function renderEventHandlers(eventType) {
    const handlersContainer = document.getElementById(`handlers-${eventType}`);
    if (!handlersContainer) return;

    const eventConfig = (currentConfig.hookHandlers || {})[eventType] || { enabled: true, handlers: [] };
    const handlers = eventConfig.handlers || [];

    if (handlers.length === 0) {
        handlersContainer.innerHTML = '<p style="font-size: 12px; color: var(--text-faint); font-style: italic; padding: 8px 0;">No handlers configured. Click "+ Add Handler" to create one.</p>';
    } else {
        handlersContainer.innerHTML = handlers.map((h, idx) => renderHandlerRow(eventType, h, idx, false)).join('');
    }
}

async function saveHookHandlers() {
    if (!currentConfig || !hookHandlersDirty) return;

    const btn = document.getElementById('saveHookHandlersBtn');
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Saving...';
    }

    try {
        // Build a full config to PUT, merging current config with hook handler changes
        const updated = JSON.parse(JSON.stringify(currentConfig));

        // Clean up empty prompt templates (convert null/empty to undefined so JSON omits them)
        if (updated.hookHandlers) {
            for (const eventType of Object.keys(updated.hookHandlers)) {
                const ec = updated.hookHandlers[eventType];
                if (ec && ec.handlers) {
                    ec.handlers.forEach(h => {
                        if (!h.promptTemplate) {
                            delete h.promptTemplate;
                        }
                        // Clean up the config dict if empty
                        if (h.config && Object.keys(h.config).length === 0) {
                            delete h.config;
                        }
                    });
                }
            }
        }

        await fetch('/api/config', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(updated)
        });

        currentConfig = updated;
        hookHandlersDirty = false;
        updateHookHandlersSaveButton();

        // Auto-sync: reinstall hooks in Claude's settings.json so they match the updated config
        try {
            var hooksStatus = await (await fetch('/api/hooks/status')).json();
            if (hooksStatus.installed) {
                await fetch('/api/hooks/install', { method: 'POST' });
                Toast.show('Hook Handlers Saved', 'Configuration saved and Claude hooks updated.', 'success');
            } else {
                Toast.show('Hook Handlers Saved', 'Configuration saved. Install hooks from Dashboard to activate.', 'success');
            }
        } catch {
            Toast.show('Hook Handlers Saved', 'Configuration saved (hooks sync skipped).', 'success');
        }
    } catch (error) {
        Toast.show('Save Failed', error.message, 'danger');
    } finally {
        if (btn) {
            btn.textContent = 'Save Handlers';
            updateHookHandlersSaveButton();
        }
    }
}

/* ==========================================================================
   Utilities
   ========================================================================== */

function escapeHtml(str) {
    if (!str) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}

function escapeAttr(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

document.addEventListener('DOMContentLoaded', loadConfig);
