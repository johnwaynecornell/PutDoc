(function () {
    function uuid() { return 'p' + (crypto.randomUUID ? crypto.randomUUID().replace(/-/g,'') : (Date.now()+Math.random()).toString(36)); }

    function ensurePuid(el) { if (!el.dataset.puid) el.dataset.puid = uuid(); return el.dataset.puid; }

    function cssPathFor(el, root) {
        const parts = [];
        let node = el;
        while (node && node.nodeType === 1 && node !== root) {
            const tag = node.tagName.toLowerCase();
            let ix = 1, sib = node;
            while ((sib = sib.previousElementSibling) != null) if (sib.tagName === node.tagName) ix++;
            parts.unshift(`${tag}:nth-of-type(${ix})`);
            node = node.parentElement;
        }
        return parts.join(' > ');
    }

    function injectToolbar(el, actions) {
        const old = el.querySelector(':scope > .putdoc-toolbar'); if (old) old.remove();
        const bar = document.createElement('div');
        bar.className = 'putdoc-toolbar';
        bar.style.cssText = 'position:absolute; top:6px; right:6px; display:flex; gap:6px; z-index:5;';
        for (const a of actions) {
            const btn = document.createElement('button');
            btn.type = 'button'; btn.textContent = a.label; btn.className = 'putdoc-btn'; btn.dataset.action = a.action;
            bar.appendChild(btn);
        }
        if (getComputedStyle(el).position === 'static') el.style.position = 'relative';
        el.prepend(bar);
    }

    window.putdocEnhance = function (container, dotnetRef) {
        if (!container) return;
        container.dataset.putdocRoot = '1';

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

        if (container._putdocBound) return;
        container._putdocBound = true;

        container.addEventListener('click', async (ev) => {
            const btn = ev.target && ev.target.closest && ev.target.closest('.putdoc-btn'); if (!btn) return;
            const host = btn.closest('.slf-card, .slf-brick, .prompt_area, pre'); if (!host) return;

            const action = btn.dataset.action;
            if (action === 'copy-code' && host.tagName.toLowerCase() === 'pre') {
                const code = host.querySelector('code'); if (code) await navigator.clipboard.writeText(code.innerText);
                return;
            }

            const puid = ensurePuid(host);
            const path = cssPathFor(host, container);   // <-- send fallback path too
            try {
                await dotnetRef.invokeMethodAsync('OnDomAction', action, puid, path);
            } catch (e) { console.error('putdoc invoke failed', e); }
        }, { passive: true });
    };

    window.putdoc = window.putdoc || {};
    window.putdoc.readClipboardText = async function () {
        try {
            // Prefer the modern async clipboard API
            if (navigator.clipboard && navigator.clipboard.readText) {
                return await navigator.clipboard.readText();
            }
        } catch (e) {
            console.warn('Clipboard readText failed', e);
        }
        return '';
    };

})();
