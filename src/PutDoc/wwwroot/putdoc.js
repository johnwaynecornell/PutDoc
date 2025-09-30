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

    window.putdocLayout = (function () {
        function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }
        function px(n) { return `${Math.round(n)}px`; }

        function initSplitters(container) {
            if (!container || container._splittersBound) return;
            container._splittersBound = true;

            // Restore saved sizes
            try {
                const savedW = localStorage.getItem('putdoc.indexW');
                const savedH = localStorage.getItem('putdoc.editorH');
                if (savedW) container.style.setProperty('--index-w', savedW);
                if (savedH) container.style.setProperty('--editor-h', savedH);
            } catch {}

            const vSplit = container.querySelector('[data-role="v-split"]');
            const hSplit = container.querySelector('[data-role="h-split"]');

            // Drag helpers
            function startDrag(e, axis) {
                e.preventDefault();
                const rect = container.getBoundingClientRect();
                const startX = (e.touches ? e.touches[0].clientX : e.clientX);
                const startY = (e.touches ? e.touches[0].clientY : e.clientY);
                const startW = parseFloat(getComputedStyle(container).getPropertyValue('--index-w')) || 420;
                const startH = parseFloat(getComputedStyle(container).getPropertyValue('--editor-h')) || 320;

                // Prevent text selection while dragging
                const prevSel = document.body.style.userSelect;
                document.body.style.userSelect = 'none';

                function onMove(ev) {
                    const x = (ev.touches ? ev.touches[0].clientX : ev.clientX);
                    const y = (ev.touches ? ev.touches[0].clientY : ev.clientY);

                    if (axis === 'x') {
                        // width from left edge to cursor
                        const dx = x - rect.left;
                        // leave room for divider + right content; clamp 240px..60% viewport
                        const w = clamp(dx, 240, Math.min(window.innerWidth * 0.6, rect.width - 240));
                        container.style.setProperty('--index-w', px(w));
                    } else {
                        // height within right grid: top of right section equals container top in this layout
                        const dy = y - rect.top;
                        const h = clamp(dy, 200, Math.min(window.innerHeight * 0.75, rect.height - 200));
                        container.style.setProperty('--editor-h', px(h));
                    }
                }

                function onUp() {
                    // persist sizes
                    try {
                        const iw = getComputedStyle(container).getPropertyValue('--index-w').trim();
                        const eh = getComputedStyle(container).getPropertyValue('--editor-h').trim();
                        localStorage.setItem('putdoc.indexW', iw);
                        localStorage.setItem('putdoc.editorH', eh);
                    } catch {}
                    // cleanup
                    document.removeEventListener('mousemove', onMove);
                    document.removeEventListener('mouseup', onUp);
                    document.removeEventListener('touchmove', onMove);
                    document.removeEventListener('touchend', onUp);
                    document.body.style.userSelect = '';
                }

                document.addEventListener('mousemove', onMove);
                document.addEventListener('mouseup', onUp);
                document.addEventListener('touchmove', onMove, { passive: false });
                document.addEventListener('touchend', onUp);
            }

            if (vSplit) {
                vSplit.addEventListener('mousedown', (e) => startDrag(e, 'x'));
                vSplit.addEventListener('touchstart', (e) => startDrag(e, 'x'), { passive: false });
                // keyboard: left/right to resize
                vSplit.addEventListener('keydown', (e) => {
                    const step = (e.shiftKey ? 40 : 16);
                    if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(container).getPropertyValue('--index-w')) || 420;
                        const delta = (e.key === 'ArrowLeft' ? -step : step);
                        const w = clamp(cur + delta, 240, Math.min(window.innerWidth * 0.6, container.getBoundingClientRect().width - 240));
                        container.style.setProperty('--index-w', px(w));
                        try { localStorage.setItem('putdoc.indexW', px(w)); } catch {}
                    }
                });
            }

            if (hSplit) {
                hSplit.addEventListener('mousedown', (e) => startDrag(e, 'y'));
                hSplit.addEventListener('touchstart', (e) => startDrag(e, 'y'), { passive: false });
                // keyboard: up/down to resize
                hSplit.addEventListener('keydown', (e) => {
                    const step = (e.shiftKey ? 40 : 16);
                    if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(container).getPropertyValue('--editor-h')) || 320;
                        const delta = (e.key === 'ArrowUp' ? -step : step);
                        const h = clamp(cur + delta, 200, Math.min(window.innerHeight * 0.75, container.getBoundingClientRect().height - 200));
                        container.style.setProperty('--editor-h', px(h));
                        try { localStorage.setItem('putdoc.editorH', px(h)); } catch {}
                    }
                });
            }
        }

        return { initSplitters };
    })();

    window.putdocHeader = (function () {
        function setHeaderVar() {
            const hdr = document.querySelector('.top-row'); // default Blazor header
            const h = hdr ? hdr.offsetHeight : 0;
            document.documentElement.style.setProperty('--topbar-h', `${h}px`);
        }
        function init() {
            setHeaderVar();
            // Recompute on resize / font load / layout changes
            window.addEventListener('resize', setHeaderVar);
            // Some layouts change height after first render
            setTimeout(setHeaderVar, 0);
        }
        return { init };
    })();


    window.putdocText = window.putdocText || {};


    window.putdocText.bindTabIndent = function (ta) {
        if (!ta || ta._putdocTabBound) return;
        ta._putdocTabBound = true;
        ta.addEventListener('keydown', function (e) {
            if (e.key === 'Tab') {
                e.preventDefault();
                window.putdocText.indent(ta, e.shiftKey);
            }
        });
    };

    window.putdocText.indent= function (ta, outdent) {
        
        const el = ta;
        const start = el.selectionStart, end = el.selectionEnd;
        const value = el.value;
        const nl = value.lastIndexOf('\n', start - 1) + 1;
        if (start !== end) {
            // block indent/outdent
            const lines = value.slice(nl, end).split('\n');
            const mod = lines.map(line => {
                if (outdent) return line.startsWith('  ') ? line.slice(2) : line;
                return '  ' + line;
            }).join('\n');
            const before = value.slice(0, nl);
            const after = value.slice(end);
            el.value = before + mod + after;
            const delta = mod.length - (value.slice(nl, end).length);
            el.selectionStart = nl;
            el.selectionEnd = end + delta;
        } else {
            // single caret
            const before = value.slice(0, start);
            const after = value.slice(end);
            if (outdent && before.endsWith('  ')) {
                el.value = before.slice(0, -2) + after;
                el.selectionStart = el.selectionEnd = start - 2;
            } else if (!outdent) {
                el.value = before + '  ' + after;
                el.selectionStart = el.selectionEnd = start + 2;
            }
        }
        // fire input so Blazor picks it up
        el.dispatchEvent(new Event('input', { bubbles: true }));
    };

    window.putdocText.getSel = (ta) => {
        return {
            start: ta.selectionStart ?? 0,
            end: ta.selectionEnd ?? 0 //ta.selectionStart ?? 0
        };
    };
    //window.putdocText.getSel = (ta) => [ta.selectionStart, ta.selectionEnd];
    window.putdocText.setSel = (ta, s, e) => { ta.selectionStart = s; ta.selectionEnd = e; ta.focus(); };

    window.putdocText.insertAtCaret = (ta, text) => {
        const s = ta.selectionStart, e = ta.selectionEnd;
        ta.value = ta.value.slice(0, s) + text + ta.value.slice(e);
        const pos = s + text.length;
        ta.selectionStart = ta.selectionEnd = pos;
        ta.dispatchEvent(new Event('input', { bubbles: true }));
        ta.focus();
    };

    window.putdocEnh = (function () {
        let started = false;

        async function ensureBlazorStarted() {
            if (started) return;
            started = true;
            /* await Blazor.start(); */
        }

        // Define a custom element that mounts a Blazor component into itself
        class PutDocToolbar extends HTMLElement {
            async connectedCallback() {
                // Create a host for the Blazor root component
                if (!this._host) {
                    this._host = document.createElement('div');
                    this.appendChild(this._host);
                }
                await ensureBlazorStarted();
                const parms = {
                    SnippetId: this.getAttribute('snippet-id'),
                    Puid: this.getAttribute('puid'),
                    Kind: this.getAttribute('kind')
                };
                
                // Mount the Blazor component into this element
                Blazor.rootComponents.add(this._host, 'putdoc.toolbar', parms);
            }
        }
        customElements.get('putdoc-toolbar') || customElements.define('putdoc-toolbar', PutDocToolbar);
        
        // Ensure container can host an absolutely positioned toolbar
        function ensurePositioned(el) {
            const cs = getComputedStyle(el);
            if (cs.position === 'static') el.style.position = 'relative';
        }

        // Ensure element has a puid; use data-puid attribute
        function ensurePuid(el) {
            if (!el.getAttribute('data-puid')) {
                el.setAttribute('data-puid', crypto.randomUUID());
            }
            return el.getAttribute('data-puid');
        }

        // Public: copy outerHTML of element by puid
        async function copyByPuid(puid) {
            const target = document.querySelector(`[data-puid="${puid}"]`);
            if (!target) return;
            const html = target.outerHTML;
            try {
                await navigator.clipboard.writeText(html);
            } catch {
                // fallback
                const ta = document.createElement('textarea');
                ta.value = html; document.body.appendChild(ta);
                ta.select(); document.execCommand('copy'); ta.remove();
            }
        }

        // Enhance a container: add toolbar to each recognized element
        function enhance(container, snippetId) {
            if (!container) return;
            const selectors = '.slf-card, .slf-brick, .prompt_area, pre';
            container.querySelectorAll(selectors).forEach(el => {
                if (el.querySelector(':scope > putdoc-toolbar')) return; // already enhanced for this el

                ensurePositioned(el);
                const puid = ensurePuid(el);
                const toolbar = document.createElement('putdoc-toolbar');
                toolbar.setAttribute('snippet-id', snippetId);
                toolbar.setAttribute('puid', puid);
                toolbar.setAttribute('kind', (el.classList[0] || el.tagName.toLowerCase()));
                el.prepend(toolbar); // place at top-right (absolute inside InlineToolbar)
            });
        }

        function enhanceById(id) {
            const el = document.getElementById(id);
            if (!el) return;
            const sid = el.getAttribute('data-snippet-id') || '';
            enhance(el, sid);
        }

        // Watch for dynamic changes (optional)
        function observe(container, snippetId) {
            const mo = new MutationObserver(() => enhance(container, snippetId));
            mo.observe(container, { childList: true, subtree: true });
            enhance(container, snippetId);
            return mo;
        }

        function observeById(id) {
            const el = document.getElementById(id);
            if (!el) return;
            const sid = el.getAttribute('data-snippet-id') || '';
            observe(el, sid);
        }

        function sanitizeForExport(node) {
            const clone = node.cloneNode(true);

            // Remove any injected toolbars
            clone.querySelectorAll('putdoc-toolbar').forEach(n => n.remove());

            // Remove data-puid from clone + descendants
            if (clone.removeAttribute) clone.removeAttribute('data-puid');
            clone.querySelectorAll('[data-puid]').forEach(n => n.removeAttribute('data-puid'));

            // Optional: strip other editor-only artifacts
            clone.querySelectorAll('[contenteditable]').forEach(n => n.removeAttribute('contenteditable'));
            // ...add more removals if you have other markers

            return clone.outerHTML;
        }

        async function copyByPuidClean(puid) {
            const target = document.querySelector(`[data-puid="${puid}"]`);
            if (!target) return;
            const html = sanitizeForExport(target);
            try {
                await navigator.clipboard.writeText(html);
            } catch (_) {
                const ta = document.createElement('textarea');
                ta.value = html; document.body.appendChild(ta);
                ta.select(); document.execCommand('copy'); ta.remove();
            }
        }

        let __selectedPuid = null;

        function clearSelected() {
            if (!__selectedPuid) return;
            document.querySelectorAll('[data-selected="true"]').forEach(n => n.removeAttribute('data-selected'));
            __selectedPuid = null;
        }

        function markSelected(puid) {
            clearSelected();
            const el = document.querySelector(`[data-puid="${puid}"]`);
            if (el) {
                el.setAttribute('data-selected', 'true');
                __selectedPuid = puid;
            }
        }

// expose it
        return {
            enhance, observe, enhanceById, observeById,
            copyByPuid: copyByPuidClean,
            markSelected
        };
    })();
    
    (function () {
        function onReady(fn) {
            if (document.readyState !== 'loading') fn();
            else document.addEventListener('DOMContentLoaded', fn);
        }

        onReady(async () => {
            // Start Blazor once
            try {
                if (!window.__pdBlazorStarted) {
                    window.__pdBlazorStarted = true;
                    await Blazor.start();  // <-- critical when autostart="false"
                }
            } catch (e) {
                console.error('Blazor.start failed', e);
            }

            // Your initializers (safe if they’re idempotent)
            //try { window.putdocHeader?.init(); } catch {}
            try {
                const layout = document.getElementById('putdocLayout');
                if (layout) window.putdocLayout?.initSplitters(layout);
            } catch {}
        });
    })(); 

})();
