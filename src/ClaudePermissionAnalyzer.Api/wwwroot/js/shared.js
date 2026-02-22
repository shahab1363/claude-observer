/* ==========================================================================
   Shared Utilities - Theme, Toasts, Keyboard Shortcuts, Connection Status
   ========================================================================== */

// ---- Theme Management ----
const Theme = {
    STORAGE_KEY: 'cpa-theme',

    init() {
        const saved = localStorage.getItem(this.STORAGE_KEY);
        if (saved) {
            this.set(saved);
        } else if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
            this.set('dark');
        }

        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
            if (!localStorage.getItem(this.STORAGE_KEY)) {
                this.set(e.matches ? 'dark' : 'light');
            }
        });
    },

    set(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem(this.STORAGE_KEY, theme);
        const btn = document.getElementById('themeToggle');
        if (btn) {
            btn.textContent = theme === 'dark' ? '\u2600' : '\u263E';
            btn.setAttribute('aria-label', theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode');
        }
    },

    toggle() {
        const current = document.documentElement.getAttribute('data-theme');
        this.set(current === 'dark' ? 'light' : 'dark');
    }
};

// ---- Toast Notifications ----
const Toast = {
    container: null,

    init() {
        this.container = document.getElementById('toastContainer');
        if (!this.container) {
            this.container = document.createElement('div');
            this.container.id = 'toastContainer';
            this.container.className = 'toast-container';
            this.container.setAttribute('role', 'status');
            this.container.setAttribute('aria-live', 'polite');
            document.body.appendChild(this.container);
        }
    },

    show(title, message, type = 'info', duration = 5000) {
        if (!this.container) this.init();

        const icons = {
            success: '\u2713',
            danger: '\u2717',
            warning: '\u26A0',
            info: '\u2139'
        };

        const toast = document.createElement('div');
        toast.className = `toast ${type}`;
        toast.setAttribute('role', 'alert');
        toast.innerHTML = `
            <span class="toast-icon">${icons[type] || icons.info}</span>
            <div class="toast-body">
                <div class="toast-title">${this.escapeHtml(title)}</div>
                <div class="toast-message">${this.escapeHtml(message)}</div>
            </div>
            <button class="toast-close" aria-label="Dismiss notification">&times;</button>
        `;

        toast.querySelector('.toast-close').addEventListener('click', () => this.remove(toast));
        this.container.appendChild(toast);

        if (duration > 0) {
            setTimeout(() => this.remove(toast), duration);
        }

        return toast;
    },

    remove(toast) {
        if (!toast || !toast.parentNode) return;
        toast.classList.add('removing');
        setTimeout(() => {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, 300);
    },

    escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }
};

// ---- Connection Status ----
const ConnectionStatus = {
    indicator: null,
    label: null,
    interval: null,

    init() {
        this.indicator = document.querySelector('.status-indicator');
        this.label = document.querySelector('.status-label');
        this.check();
        this.interval = setInterval(() => this.check(), 15000);
    },

    async check() {
        try {
            const response = await fetch('/api/dashboard/health', { signal: AbortSignal.timeout(5000) });
            if (response.ok) {
                this.setStatus(true);
            } else {
                this.setStatus(false);
            }
        } catch {
            this.setStatus(false);
        }
    },

    setStatus(connected) {
        if (this.indicator) {
            this.indicator.classList.toggle('active', connected);
            this.indicator.classList.toggle('disconnected', !connected);
        }
        if (this.label) {
            this.label.textContent = connected ? 'Connected' : 'Disconnected';
        }
    }
};

// ---- Keyboard Shortcuts ----
const Shortcuts = {
    modal: null,

    init() {
        this.modal = document.getElementById('shortcutsModal');

        document.addEventListener('keydown', (e) => {
            // Don't trigger shortcuts when typing in inputs
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') {
                if (e.key === 'Escape') {
                    e.target.blur();
                }
                return;
            }

            switch (e.key) {
                case '?':
                    e.preventDefault();
                    this.toggleModal();
                    break;
                case 'd':
                    e.preventDefault();
                    Theme.toggle();
                    break;
                case 'r':
                    e.preventDefault();
                    if (typeof refreshData === 'function') refreshData();
                    break;
                case '1':
                    e.preventDefault();
                    window.location.href = '/';
                    break;
                case '2':
                    e.preventDefault();
                    window.location.href = '/logs.html';
                    break;
                case '3':
                    e.preventDefault();
                    window.location.href = '/config.html';
                    break;
                case 'Escape':
                    if (this.modal && this.modal.classList.contains('active')) {
                        this.toggleModal();
                    }
                    break;
            }
        });

        if (this.modal) {
            this.modal.addEventListener('click', (e) => {
                if (e.target === this.modal) {
                    this.toggleModal();
                }
            });
        }
    },

    toggleModal() {
        if (this.modal) {
            this.modal.classList.toggle('active');
        }
    }
};

// ---- Data Fetching with Error Handling ----
async function fetchApi(url, options = {}) {
    try {
        const response = await fetch(url, {
            ...options,
            signal: options.signal || AbortSignal.timeout(10000)
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        return await response.json();
    } catch (error) {
        if (error.name === 'AbortError' || error.name === 'TimeoutError') {
            throw new Error('Request timed out. Please check your connection.');
        }
        throw error;
    }
}

// ---- Time Formatting ----
function formatTime(timestamp) {
    const date = new Date(timestamp);
    const now = new Date();
    const diff = now - date;

    if (diff < 60000) return 'just now';
    if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
    if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;

    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) +
        ' ' + date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

function formatTimestamp(timestamp) {
    return new Date(timestamp).toLocaleString(undefined, {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit'
    });
}

// ---- Score Helpers ----
function getScoreClass(score) {
    if (score >= 70) return 'high';
    if (score >= 40) return 'medium';
    return 'low';
}

function getDecisionClass(decision) {
    switch (decision) {
        case 'auto-approved': return 'approved';
        case 'denied': return 'denied';
        default: return 'no-handler';
    }
}

function getDecisionLabel(decision) {
    switch (decision) {
        case 'auto-approved': return 'Approved';
        case 'denied': return 'Denied';
        case 'no-handler': return 'Logged';
        default: return decision || 'Unknown';
    }
}

// ---- Simple Markdown Renderer (no external deps) ----
const SimpleMarkdown = {
    render(text) {
        if (!text) return '';
        var html = this.escapeHtml(text);

        // Code blocks (``` ... ```) - must be before inline patterns
        html = html.replace(/```(\w*)\n([\s\S]*?)```/g, function(m, lang, code) {
            return '<pre class="md-code-block"><code>' + code.trim() + '</code></pre>';
        });

        // Inline code
        html = html.replace(/`([^`\n]+)`/g, '<code class="md-inline-code">$1</code>');

        // Headings
        html = html.replace(/^#### (.+)$/gm, '<h4 class="md-h4">$1</h4>');
        html = html.replace(/^### (.+)$/gm, '<h3 class="md-h3">$1</h3>');
        html = html.replace(/^## (.+)$/gm, '<h2 class="md-h2">$1</h2>');
        html = html.replace(/^# (.+)$/gm, '<h1 class="md-h1">$1</h1>');

        // Bold and italic
        html = html.replace(/\*\*\*(.+?)\*\*\*/g, '<strong><em>$1</em></strong>');
        html = html.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
        html = html.replace(/\*(.+?)\*/g, '<em>$1</em>');

        // Unordered lists
        html = html.replace(/^[\-\*] (.+)$/gm, '<li class="md-li">$1</li>');
        html = html.replace(/((?:<li class="md-li">.*<\/li>\n?)+)/g, '<ul class="md-ul">$1</ul>');

        // Ordered lists
        html = html.replace(/^\d+\. (.+)$/gm, '<li class="md-oli">$1</li>');
        html = html.replace(/((?:<li class="md-oli">.*<\/li>\n?)+)/g, '<ol class="md-ol">$1</ol>');

        // Links [text](url) - only allow safe protocols
        html = html.replace(/\[([^\]]+)\]\(((?:https?:\/\/|\/)[^\)]+)\)/g, '<a href="$2" class="md-link" target="_blank" rel="noopener">$1</a>');

        // Horizontal rules
        html = html.replace(/^---+$/gm, '<hr class="md-hr">');

        // Line breaks (preserve paragraph breaks)
        html = html.replace(/\n\n/g, '</p><p class="md-p">');
        html = '<p class="md-p">' + html + '</p>';
        // Clean up empty paragraphs
        html = html.replace(/<p class="md-p"><\/p>/g, '');
        // Don't wrap block elements in p
        html = html.replace(/<p class="md-p">(<(?:h[1-4]|pre|ul|ol|hr)[^>]*>)/g, '$1');
        html = html.replace(/(<\/(?:h[1-4]|pre|ul|ol|hr)>)<\/p>/g, '$1');

        return html;
    },

    escapeHtml(str) {
        return String(str).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }
};

// ---- Initialize on DOM load ----
document.addEventListener('DOMContentLoaded', () => {
    Theme.init();
    Toast.init();
    ConnectionStatus.init();
    Shortcuts.init();

    const themeBtn = document.getElementById('themeToggle');
    if (themeBtn) {
        themeBtn.addEventListener('click', () => Theme.toggle());
    }
});
