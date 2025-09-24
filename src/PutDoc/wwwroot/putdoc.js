// wwwroot/putdoc.js
(function () {
    function uuid() { return 'p' + (crypto.randomUUID ? crypto.randomUUID().replace(/-/g,'') : (Date.now()+Math.random()).toString(36)); }

    function ensurePuid(el) {
        if (!el.dataset.puid) el.dataset.puid = uuid();
        return el.dataset.puid;
    }

    function injectToolbar(el, actions) {
        // Remove any old toolbar (idempotent)
        const old = el.querySelector(':scope > .putdoc-toolbar');
        if (old) old.remove();

        const bar = document.createElement('div');
        bar.className = 'putdoc-toolbar';
        bar.style.cssText = 'position:absolute; top:6px; right:6px; display:flex; gap:6px; z-index:5;';
        for (const a of actions) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.textContent = a.label;
            btn.className = 'putdoc-btn';
            btn.dataset.action = a.action;
            bar.appendChild(btn);
        }
        if (getComputedStyle(el).position === 'static') el.style.position = 'relative';
        el.prepend(bar);
    }

    window.putdocEnhance = function (container, dotnetRef) {
        if (!container) return;

        // Mark root
        container.dataset.putdocRoot = '1';

        // Always (re)decorate actionable elements
        const targets = container.querySelectorAll('.slf-card, .slf-brick, .prompt_area, pre');
        for (const el of targets) {
            ensurePuid(el);
            if (el.tagName.toLowerCase() === 'pre') {
                injectToolbar(el, [{ label: 'Copy', action: 'copy-code' }]);
            } else {
                injectToolbar(el, [
                    { label: 'Edit',  action: 'edit' },
                    { label: 'Clone', action: 'clone' },
                    { label: 'Del',   action: 'delete' },
                    { label: '↑',     action: 'move-up' },
                    { label: '↓',     action: 'move-down' },
                ]);
            }
        }

        // Bind the click handler exactly once per container
        if (container._putdocBound) return;
        container._putdocBound = true;

        container.addEventListener('click', async (ev) => {
            const btn = ev.target && ev.target.closest && ev.target.closest('.putdoc-btn');
            if (!btn) return;

            const host = btn.closest('.slf-card, .slf-brick, .prompt_area, pre');
            if (!host) return;

            const action = btn.dataset.action;
            const puid = ensurePuid(host);

            if (action === 'copy-code' && host.tagName.toLowerCase() === 'pre') {
                const code = host.querySelector('code');
                if (code) await navigator.clipboard.writeText(code.innerText);
                return;
            }

            try {
                await dotnetRef.invokeMethodAsync('OnDomAction', action, puid);
            } catch (e) {
                console.error('putdoc invoke failed', e);
            }
        }, { passive: true });
    };
})();
