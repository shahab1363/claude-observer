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

const MODE_BADGE_COLORS = {
    'llm-analysis': 'var(--color-info)',
    'log-only': 'var(--text-faint)',
    'context-injection': 'var(--color-warning)',
    'custom-logic': 'var(--color-success)'
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
                    data-path="llm.provider" aria-label="LLM provider"
                    onchange="updateProviderFields()">
                    <option value="anthropic-api" ${(config.llm?.provider || 'anthropic-api') === 'anthropic-api' ? 'selected' : ''}>Anthropic API (Direct)</option>
                    <option value="claude-cli" ${config.llm?.provider === 'claude-cli' ? 'selected' : ''}>Claude Code CLI (One-shot)</option>
                    <option value="claude-persistent" ${config.llm?.provider === 'claude-persistent' ? 'selected' : ''}>Claude Code CLI (Persistent)</option>
                    <option value="copilot-cli" ${config.llm?.provider === 'copilot-cli' ? 'selected' : ''}>GitHub Copilot CLI</option>
                    <option value="generic-rest" ${config.llm?.provider === 'generic-rest' ? 'selected' : ''}>Generic REST API</option>
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

            <!-- Anthropic API fields -->
            <div id="provider-anthropic-api" class="provider-fields">
                <div class="config-field">
                    <label class="config-label" for="cfg-apikey">
                        API Key
                        <small>Anthropic API key (falls back to ~/.claude/config.json)</small>
                    </label>
                    <input id="cfg-apikey" class="config-input" type="password"
                        value="${escapeAttr(config.llm?.apiKey || '')}"
                        placeholder="(uses Claude config key)"
                        data-path="llm.apiKey" aria-label="API key"
                        autocomplete="off">
                </div>
                <div class="config-field">
                    <label class="config-label" for="cfg-apibaseurl">
                        API Base URL
                        <small>Override for proxies or compatible APIs</small>
                    </label>
                    <input id="cfg-apibaseurl" class="config-input" type="text"
                        value="${escapeAttr(config.llm?.apiBaseUrl || '')}"
                        placeholder="https://api.anthropic.com"
                        data-path="llm.apiBaseUrl" aria-label="API base URL">
                </div>
            </div>

            <!-- CLI provider fields -->
            <div id="provider-cli" class="provider-fields">
                <div class="config-field">
                    <label class="config-label" for="cfg-command">
                        CLI Command
                        <small>Executable name (e.g. "claude", "copilot", "gh")</small>
                    </label>
                    <input id="cfg-command" class="config-input" type="text"
                        value="${escapeAttr(config.llm?.command || '')}"
                        placeholder="auto-detect"
                        data-path="llm.command" aria-label="CLI command">
                </div>
            </div>

            <!-- Generic REST fields -->
            <div id="provider-generic-rest" class="provider-fields">
                <div class="config-field">
                    <label class="config-label" for="cfg-rest-url">
                        REST URL
                        <small>Endpoint URL (e.g. https://api.openai.com/v1/chat/completions)</small>
                    </label>
                    <input id="cfg-rest-url" class="config-input" type="text"
                        value="${escapeAttr(config.llm?.genericRest?.url || '')}"
                        placeholder="https://api.openai.com/v1/chat/completions"
                        data-path="llm.genericRest.url" aria-label="REST URL"
                        style="width: 350px;">
                </div>
                <div class="config-field">
                    <label class="config-label" for="cfg-rest-headers">
                        Headers (JSON)
                        <small>e.g. {"Authorization": "Bearer sk-..."}</small>
                    </label>
                    <textarea id="cfg-rest-headers" class="config-input" rows="3"
                        placeholder='{"Authorization": "Bearer sk-..."}'
                        data-path="llm.genericRest.headers" aria-label="REST headers"
                        style="width: 350px; font-family: var(--font-mono); font-size: 12px;">${escapeHtml(JSON.stringify(config.llm?.genericRest?.headers || {}, null, 2))}</textarea>
                </div>
                <div class="config-field">
                    <label class="config-label" for="cfg-rest-body">
                        Body Template
                        <small>JSON with {PROMPT} placeholder</small>
                    </label>
                    <textarea id="cfg-rest-body" class="config-input" rows="5"
                        placeholder='{"model":"gpt-4","messages":[{"role":"user","content":"{PROMPT}"}]}'
                        data-path="llm.genericRest.bodyTemplate" aria-label="REST body template"
                        style="width: 350px; font-family: var(--font-mono); font-size: 12px;">${escapeHtml(config.llm?.genericRest?.bodyTemplate || '')}</textarea>
                </div>
                <div class="config-field">
                    <label class="config-label" for="cfg-rest-path">
                        Response Path
                        <small>Dot-notation to extract text (e.g. choices[0].message.content)</small>
                    </label>
                    <input id="cfg-rest-path" class="config-input" type="text"
                        value="${escapeAttr(config.llm?.genericRest?.responsePath || '')}"
                        placeholder="choices[0].message.content"
                        data-path="llm.genericRest.responsePath" aria-label="Response path">
                </div>
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

    // Show/hide provider-specific fields
    updateProviderFields();
}

function updateProviderFields() {
    const provider = document.getElementById('cfg-provider')?.value || 'anthropic-api';

    const apiFields = document.getElementById('provider-anthropic-api');
    const cliFields = document.getElementById('provider-cli');
    const restFields = document.getElementById('provider-generic-rest');

    if (apiFields) apiFields.style.display = provider === 'anthropic-api' ? 'block' : 'none';
    if (cliFields) cliFields.style.display = ['claude-cli', 'claude-persistent', 'copilot-cli'].includes(provider) ? 'block' : 'none';
    if (restFields) restFields.style.display = provider === 'generic-rest' ? 'block' : 'none';
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

        // Skip hidden provider fields to avoid overwriting config with empty values
        const providerSection = input.closest('.provider-fields');
        if (providerSection && providerSection.style.display === 'none') return;

        const parts = path.split('.');
        let obj = updated;
        for (let i = 0; i < parts.length - 1; i++) {
            if (!obj[parts[i]]) obj[parts[i]] = {};
            obj = obj[parts[i]];
        }

        const key = parts[parts.length - 1];
        if (input.type === 'number') {
            obj[key] = parseInt(input.value, 10);
        } else if (key === 'headers' && input.tagName === 'TEXTAREA') {
            // Parse JSON for headers field
            try {
                obj[key] = JSON.parse(input.value || '{}');
            } catch {
                obj[key] = {};
            }
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
        <div class="hook-event-card" data-event="${escapeAttr(eventType)}">
            <div class="hook-event-header">
                <div class="hook-event-title">
                    <h4>${escapeHtml(eventType)}</h4>
                    <label class="hook-event-toggle">
                        <input type="checkbox" ${enabled ? 'checked' : ''}
                            onchange="toggleHookEvent('${escapeAttr(eventType)}', this.checked)"
                            style="cursor: pointer;">
                        Enabled
                    </label>
                </div>
                <button class="btn btn-sm" onclick="addHandler('${escapeAttr(eventType)}')" style="font-size: 11px;">+ Add Handler</button>
            </div>
            <p class="hook-event-desc">${escapeHtml(description)}</p>
            <div id="handlers-${escapeAttr(eventType)}">
                ${handlers.length === 0
                    ? '<p class="hook-empty-msg">No handlers configured. Click "+ Add Handler" to create one.</p>'
                    : handlers.map((h, idx) => renderHandlerRow(eventType, h, idx, false)).join('')
                }
            </div>
        </div>`;
    }

    container.innerHTML = html;
}

function renderHandlerRow(eventType, handler, index, editing) {
    if (editing) {
        return renderHandlerEditRow(eventType, handler, index);
    }

    const safeEvent = escapeAttr(eventType);
    const rowId = `handler-${safeEvent}-${index}`;
    const badgeColor = MODE_BADGE_COLORS[handler.mode] || 'var(--text-faint)';
    const promptFile = handler.promptTemplate ? handler.promptTemplate.replace(/^.*[\\\/]/, '') : '';

    return `
    <div id="${rowId}" class="handler-row">
        <div class="handler-row-details">
            <span class="handler-name" title="Handler name">${escapeHtml(handler.name || '(unnamed)')}</span>
            <span class="handler-mode-badge" style="background: ${badgeColor};" title="Mode">${escapeHtml(handler.mode || 'log-only')}</span>
            <code class="handler-matcher" title="Matcher pattern: ${escapeAttr(handler.matcher || '*')}">${escapeHtml(handler.matcher || '*')}</code>
            <span class="handler-thresholds" title="Thresholds: Strict / Moderate / Permissive">S:<strong>${handler.thresholdStrict || 95}</strong> M:<strong>${handler.thresholdModerate || 85}</strong> P:<strong>${handler.thresholdPermissive || 70}</strong></span>
            ${handler.autoApprove ? '<span class="handler-auto-approve" title="Auto-approve enabled">Auto-approve</span>' : ''}
            ${promptFile ? `<span class="handler-prompt-label" title="Prompt: ${escapeAttr(handler.promptTemplate)}">Prompt: ${escapeHtml(promptFile)}</span>` : ''}
        </div>
        <div class="handler-actions">
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
    <div id="${rowId}" class="handler-edit-card">
        <div class="handler-edit-grid">
            <div>
                <label class="handler-edit-label">Name</label>
                <input type="text" id="${rowId}-name" value="${escapeAttr(handler.name || '')}"
                    placeholder="e.g. bash-analyzer"
                    class="handler-edit-input handler-edit-input-mono">
            </div>
            <div>
                <label class="handler-edit-label">Matcher Pattern (regex)</label>
                <input type="text" id="${rowId}-matcher" value="${escapeAttr(handler.matcher || '')}"
                    placeholder="e.g. Bash|Write or *"
                    class="handler-edit-input handler-edit-input-mono">
            </div>
            <div>
                <label class="handler-edit-label">Mode</label>
                <select id="${rowId}-mode" class="handler-edit-input">
                    ${modeOptions}
                </select>
            </div>
            <div>
                <label class="handler-edit-label">Prompt Template</label>
                <select id="${rowId}-prompt" class="handler-edit-input">
                    <option value="">(none)</option>
                    ${promptOptions}
                </select>
            </div>
            <div>
                <label class="handler-edit-label">Strict Threshold</label>
                <input type="number" id="${rowId}-thresholdStrict" value="${handler.thresholdStrict != null ? handler.thresholdStrict : 95}"
                    min="0" max="100" class="handler-edit-input handler-edit-input-mono">
            </div>
            <div>
                <label class="handler-edit-label">Moderate Threshold</label>
                <input type="number" id="${rowId}-thresholdModerate" value="${handler.thresholdModerate != null ? handler.thresholdModerate : 85}"
                    min="0" max="100" class="handler-edit-input handler-edit-input-mono">
            </div>
            <div>
                <label class="handler-edit-label">Permissive Threshold</label>
                <input type="number" id="${rowId}-thresholdPermissive" value="${handler.thresholdPermissive != null ? handler.thresholdPermissive : 70}"
                    min="0" max="100" class="handler-edit-input handler-edit-input-mono">
            </div>
            <div class="handler-edit-checkbox">
                <label>
                    <input type="checkbox" id="${rowId}-autoapprove" ${handler.autoApprove ? 'checked' : ''}
                        style="cursor: pointer;">
                    Auto-approve when safe
                </label>
            </div>
        </div>
        <div class="handler-edit-actions">
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
        handlersContainer.innerHTML = '<p class="hook-empty-msg">No handlers configured. Click "+ Add Handler" to create one.</p>';
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
