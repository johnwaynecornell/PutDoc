// wwwroot/putdoc.js
(function () {
    function uuid() { return 'p' + crypto.randomUUID().replace(/-/g,''); }

    function ensurePuid(el) {
        if (!el.dataset.puid) el.dataset.puid = uuid();
        return el.dataset.puid;
    }

    function injectToolbar(el, actions) {
        // remove old toolbar if any (idempotent)
        const old = el.querySelector(':scope > .putdoc-toolbar');
        if (old) old.remove();

        const bar = document.createElement('div');
        bar.className = 'putdoc-toolbar';
        bar.style.cssText = 'position:absolute; top:6px; right:6px; display:flex; gap:6px; z-index:5;';
        actions.forEach(a => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.textContent = a.label;
            btn.dataset.action = a.action;
            btn.className = 'putdoc-btn';
            bar.appendChild(btn);
        });
        el.style.position = el.style.position || 'relative';
        el.prepend(bar);
    }

    window.putdocEnhance = function (container, dotnetRef) {
        if (!container) return;

        // Mark root once
        container.dataset.putdocRoot = "1";

        // Decorate targets and ensure stable IDs (data-puid)
        const targets = container.querySelectorAll('.slf-card, .slf-brick, .prompt_area, pre');
        targets.forEach(el => {
            ensurePuid(el);
            let actions = [];
            if (el.tagName.toLowerCase() === 'pre') {
                actions = [{ label:'Copy', action:'copy-code' }];
            } else {
                actions = [
                    { label:'Edit',  action:'edit' },
                    { label:'Clone', action:'clone' },
                    { label:'Del',   action:'delete' },
                    { label:'↑',     action:'move-up' },
                    { label:'↓',     action:'move-down' },
                ];
            }
            injectToolbar(el, actions);
        });

        // Bind exactly once
        if (container.dataset.putdocBound === '1') return;
        container.dataset.putdocBound = '1';

        container.addEventListener('click', async (ev) => {
            const btn = ev.target.closest('.putdoc-btn');
            if (!btn) return;
            const host = btn.closest('.slf-card, .slf-brick, .prompt_area, pre');
            if (!host) return;

            const action = btn.dataset.action;
            if (action === 'copy-code' && host.tagName.toLowerCase() === 'pre') {
                const code = host.querySelector('code');
                if (code) await navigator.clipboard.writeText(code.innerText);
                return;
            }
            const puid = host.dataset.puid || ensurePuid(host);
            await dotnetRef.invokeMethodAsync('OnDomAction', action, puid);
        }, { passive: true });
    };
})();
