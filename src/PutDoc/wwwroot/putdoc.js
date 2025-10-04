(function () {
    function uuid() { return 'p' + (crypto.randomUUID ? crypto.randomUUID().replace(/-/g,'') : (Date.now()+Math.random()).toString(36)); }

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

    window.putdoc.getClientId = function() {
        try {
            let id = localStorage.getItem("pd.clientId");
            if (!id) { id = (crypto.randomUUID ? crypto.randomUUID() : (Date.now()+Math.random()).toString(36)); localStorage.setItem("pd.clientId", id); }
            return id;
        } catch { return "anon-" + Math.random().toString(36).slice(2); }
    };

    window.putdoc.getSessionId = function () {
        try {
            let id = sessionStorage.getItem("pd.sessionId");
            if (!id) { id = (crypto.randomUUID ? crypto.randomUUID() : Math.random().toString(36).slice(2)); sessionStorage.setItem("pd.sessionId", id); }
            return id;
        } catch { return "sess-" + Math.random().toString(36).slice(2); }
    };


    window.putdocLayout = (function () {
        function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }
        function px(n) { return `${Math.round(n)}px`; }


        function resolveEl(elOrSel) {
            if (!elOrSel) return null;
            if (elOrSel instanceof Element) return elOrSel;
            // Handle Blazor ElementReference-like proxies
            if (typeof elOrSel === "object" && elOrSel.querySelector && elOrSel.getBoundingClientRect) return elOrSel;
            if (typeof elOrSel === "string") return document.getElementById(elOrSel) || document.querySelector(elOrSel);
            return null;
        }

        function initSplitters(containerArg) {
            let container =
                resolveEl(containerArg) ||
                document.getElementById("putdocLayout") ||
                document.querySelector(".putdoc-layout") ||
                document.querySelector('[data-putdoc-layout]');

            if (!container) {
                console.warn("putdocLayout.initSplitters: container not found", containerArg);
                return;
            }

            // If not attached yet, try next frame
            if (!container.isConnected) {
                requestAnimationFrame(() => initSplitters(container));
                return;
            }

            // Restore saved sizes once per container
            if (!container._sizesRestored) {
                container._sizesRestored = true;
                try {
                    const savedW = localStorage.getItem("putdoc.indexW");
                    const savedH = localStorage.getItem("putdoc.editorH");
                    if (savedW) container.style.setProperty("--index-w", savedW);
                    if (savedH) container.style.setProperty("--editor-h", savedH);
                } catch {}
            }

            const vSplit = container.querySelector('[data-role="v-split"]');
            const hSplit = container.querySelector('[data-role="h-split"]');

            const clamp = (v, min, max) => Math.max(min, Math.min(max, v));
            const px = (n) => `${Math.round(n)}px`;

            function bindV(div) {
                if (!div || div._pdBound) return;
                div._pdBound = true;

                div.addEventListener("pointerdown", (e) => {
                    // either remove this line OR keep it and immediately focus:
                    // e.preventDefault();
                    div.focus({ preventScroll: true });            // <-- ensure the element is focused
                    div.setPointerCapture(e.pointerId);

                    const rect = container.getBoundingClientRect();
                    let moved = false;

                    const onMove = (ev) => {
                        const x = ev.clientX;
                        const dx = x - rect.left;
                        const w = clamp(dx, 240, Math.min(window.innerWidth * 0.6, rect.width - 240));
                        container.style.setProperty("--index-w", px(w));
                        moved = true;
                    };
                    const onUp = () => {
                        div.releasePointerCapture(e.pointerId);
                        window.removeEventListener("pointermove", onMove);
                        window.removeEventListener("pointerup", onUp);
                        if (moved) {
                            try {
                                const iw = getComputedStyle(container).getPropertyValue("--index-w").trim();
                                localStorage.setItem("putdoc.indexW", iw);
                            } catch {}
                        }
                    };

                    window.addEventListener("pointermove", onMove);
                    window.addEventListener("pointerup", onUp);
                });

                // keyboard support (unchanged)
                div.addEventListener("keydown", (e) => {
                    const step = e.shiftKey ? 40 : 16;
                    const key = e.key || e.code;
                    if (key === "ArrowLeft" || key === "Left") {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(container).getPropertyValue("--index-w")) || 420;
                        const w = clamp(cur - step, 240, Math.min(window.innerWidth * 0.6, container.getBoundingClientRect().width - 240));
                        container.style.setProperty("--index-w", px(w));
                        try { localStorage.setItem("putdoc.indexW", px(w)); } catch {}
                    } else if (key === "ArrowRight" || key === "Right") {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(container).getPropertyValue("--index-w")) || 420;
                        const w = clamp(cur + step, 240, Math.min(window.innerWidth * 0.6, container.getBoundingClientRect().width - 240));
                        container.style.setProperty("--index-w", px(w));
                        try { localStorage.setItem("putdoc.indexW", px(w)); } catch {}
                    }
                });
            }

            function bindH(div) {
                if (!div || div._pdBound) return;
                div._pdBound = true;

                div.addEventListener("pointerdown", (e) => {
                    // e.preventDefault();
                    div.focus({ preventScroll: true });            // <-- ensure focus
                    div.setPointerCapture(e.pointerId);

                    const rect = container.getBoundingClientRect();
                    let moved = false;

                    const onMove = (ev) => {
                        const y = ev.clientY;
                        const dy = y - rect.top;
                        const h = clamp(dy, 200, Math.min(window.innerHeight * 0.75, rect.height - 200));
                        container.style.setProperty("--editor-h", px(h));
                        moved = true;
                    };
                    const onUp = () => {
                        div.releasePointerCapture(e.pointerId);
                        window.removeEventListener("pointermove", onMove);
                        window.removeEventListener("pointerup", onUp);
                        if (moved) {
                            try {
                                const eh = getComputedStyle(container).getPropertyValue("--editor-h").trim();
                                localStorage.setItem("putdoc.editorH", eh);
                            } catch {}
                        }
                    };

                    window.addEventListener("pointermove", onMove);
                    window.addEventListener("pointerup", onUp);
                });

                div.addEventListener("keydown", (e) => {
                    const step = e.shiftKey ? 40 : 16;
                    const key = e.key || e.code;
                    if (key === "ArrowUp" || key === "Up") {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(container).getPropertyValue("--editor-h")) || 320;
                        const h = clamp(cur - step, 200, Math.min(window.innerHeight * 0.75, container.getBoundingClientRect().height - 200));
                        container.style.setProperty("--editor-h", px(h));
                        try { localStorage.setItem("putdoc.editorH", px(h)); } catch {}
                    } else if (key === "ArrowDown" || key === "Down") {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(container).getPropertyValue("--editor-h")) || 320;
                        const h = clamp(cur + step, 200, Math.min(window.innerHeight * 0.75, container.getBoundingClientRect().height - 200));
                        container.style.setProperty("--editor-h", px(h));
                        try { localStorage.setItem("putdoc.editorH", px(h)); } catch {}
                    }
                });
            }

            bindV(vSplit);
            bindH(hSplit);
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


    // Ensure namespace
    window.putdocText = window.putdocText || (window.putdocText = ({}));

    (function(ns){
        // WeakMap: textarea -> { start, end }
        const caretCache = new WeakMap();

        function updateCache(ta) {
            try {
                caretCache.set(ta, { start: ta.selectionStart ?? 0, end: ta.selectionEnd ?? 0 });
            } catch { /* noop */ }
        }

        ns.initEditor = function(ta) {
            // keep your existing tab indent if available
            if (typeof ns.bindTabIndent === 'function') {
                try { ns.bindTabIndent(ta); } catch {}
            }
            // seed + track caret on typical events
            updateCache(ta);
            // In putdocText module (augment the tracker you added earlier)
            ['beforeinput','input','keyup','mouseup','pointerup','select','focus'].forEach(evt =>
                ta.addEventListener(evt, () => updateCache(ta))
            );

            // if focus is regained, refresh
            ta.addEventListener('focus', () => updateCache(ta), { passive: true });
        };

        ns.getCachedSel = function(ta){
            const s = caretCache.get(ta);
            if (s) return s;
            // fallback to live read
            try { return { start: ta.selectionStart ?? 0, end: ta.selectionEnd ?? 0 }; }
            catch { return { start: 0, end: 0 }; }
        };

        ns.setSelSmooth = function (ta, start, end) {
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    try {
                        ta.setSelectionRange(start, end);
                        ta.focus();
                    } catch {}
                });
            });
        };
    })(window.putdocText);
    
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
        let __hub = null; // DotNetObjectReference set by ToolbarHub once
        let __currentOpen = null;
        function setHub(dotNetRef) { __hub = dotNetRef; }
        function getHub() { return __hub; }

        // Ensure container can host absolutely positioned toolbar
        function ensurePositioned(el) {
            const cs = getComputedStyle(el);
            //if (cs.position === 'static') el.style.position = 'relative';
        }

        // Ensure element has a puid
        function ensurePuid(el) {
            if (!el.getAttribute('data-puid')) {
                el.setAttribute('data-puid', crypto.randomUUID());
            }
            return el.getAttribute('data-puid');
        }

        // --- Copy helpers (unchanged) ---
        function sanitizeForExport(node) {
            const clone = node.cloneNode(true);

            // Remove any injected toolbars
            clone.querySelectorAll('putdoc-toolbar').forEach(n => n.remove());

            // Remove data-puid + selection flags
            if (clone.removeAttribute) clone.removeAttribute('data-puid');
            clone.querySelectorAll('[data-puid]').forEach(n => n.removeAttribute('data-puid'));
            if (clone.removeAttribute) clone.removeAttribute('data-selected');
            clone.querySelectorAll('[data-selected]').forEach(n => n.removeAttribute('data-selected'));

            // Remove editor-only bits
            clone.querySelectorAll('[contenteditable]').forEach(n => n.removeAttribute('contenteditable'));

            return clone.outerHTML; // <-- put this back
        }

        async function copyByPuidClean(puid) {
            const target = document.querySelector(`[data-puid="${puid}"]`);
            if (!target) return;
            const html = sanitizeForExport(target);
            try {
                await navigator.clipboard.writeText(html);
            } catch {
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

        // --- Web Component: renders buttons (no Blazor root!) ---
        // Single-open + global close
        (function ensureGlobalHandlers() {
            if (window.__pdToolbarHandlersInstalled) return;
            window.__pdToolbarHandlersInstalled = true;

            document.addEventListener('click', (evt) => {
                if (!__currentOpen) return;
                if (!__currentOpen.contains(evt.target)) closeCurrent();
            });
            document.addEventListener('keydown', (evt) => {
                if (evt.key === 'Escape') closeCurrent(true);
            });
        })();
        function closeCurrent(refocus) {
            if (!__currentOpen) return;
            const shell = __currentOpen.querySelector('.pd-inline-toolbar');
            const gear  = __currentOpen.querySelector('.pd-gear');
            if (shell) {
                shell.dataset.open = 'false';
                shell.setAttribute('aria-expanded', 'false');
            }
            if (refocus && gear) gear.focus();
            __currentOpen = null;
        }
        function openThis(host) {
            if (__currentOpen && __currentOpen !== host) closeCurrent(false);
            const shell = host.querySelector('.pd-inline-toolbar');
            if (shell) {
                shell.dataset.open = 'true';
                shell.setAttribute('aria-expanded', 'true');
            }
            __currentOpen = host;
        }

// Helper to attach handlers inside panel
        function wirePanelActions(panelEl, puid, snippetId) {
            panelEl.querySelectorAll('[data-act]').forEach(btn => {
                btn.addEventListener('click', async () => {
                    const act = btn.getAttribute('data-act');
                    if (!__hub || !act) return;

                    // For edit actions, try acquire → prompt override if denied
                    if (act === 'edit-inner' || act === 'edit-outer') {
                        const kind = (act === 'edit-outer') ? 'fragment-outer' : 'fragment-inner';
                        let res = await __hub.invokeMethodAsync('AcquireForEdit', kind, puid, snippetId, /*force*/ false);
                        if (res?.status === 'denied') {
                            // simple prompt; replace with your nicer UI if you like
                            const ok = confirm(`Held by ${res.holder?.user ?? 'someone'}. Take over?`);
                            if (!ok) return;
                            res = await __hub.invokeMethodAsync('AcquireForEdit', kind, puid, snippetId, /*force*/ true);
                            if (res?.status !== 'granted' && res?.status !== 'stolen') return;
                        }
                    }

                    const out = await __hub.invokeMethodAsync('Handle', act, puid, snippetId);
                    closeCurrent(false);
                });
            });
        }

        class PutDocToolbar extends HTMLElement {
            connectedCallback() {
                if (this._wired) return;
                this._wired = true;

                const snippetId = this.getAttribute('snippet-id') || '';
                const puid      = this.getAttribute('puid') || '';
                const kind      = this.getAttribute('kind') || '';

                this.classList.add('pd-toolbar-host');
                this.innerHTML = `
      <span class="pd-inline-toolbar" data-open="false" aria-expanded="false" aria-haspopup="menu">
        <button type="button" class="btn drop pd-gear" title="Actions" aria-label="Open toolbar">⋯</button>
        <div class="pd-toolbar-panel-slot"></div>
      </span>
    `;

                const shell = this.querySelector('.pd-inline-toolbar');
                const gear  = this.querySelector('.pd-gear');
                const slot  = this.querySelector('.pd-toolbar-panel-slot');
                let loaded  = false;

                gear?.addEventListener('click', async (e) => {
                    e.stopPropagation();
                    const isOpen = shell?.dataset.open === 'true';

                    if (!isOpen) {
                        // Opening: fetch menu HTML on first open
                        if (!loaded) {
                            if (!__hub) { /* Optional: fallback to static template here */ }
                            const html = await __hub.invokeMethodAsync('GetMenuHtml', kind, puid, snippetId);
                            // Insert without clobbering handlers; then wire
                            slot.innerHTML = html;
                            // Ensure the root gets both required classes
                            const root = slot.firstElementChild;
                            if (root) {
                                root.classList.add('menu-popover', 'pd-toolbar-panel');
                                wirePanelActions(root, puid, snippetId);
                            }
                            loaded = true;
                        }
                        openThis(this);
                    } else {
                        closeCurrent(true);
                    }
                });
            }
        }
        customElements.get('putdoc-toolbar') || customElements.define('putdoc-toolbar', PutDocToolbar);
        
        // Enhance a container: add toolbar to each recognized element
        function enhance(container, snippetId) {
            if (!container) return;
            const selectors = '.slf-card, .slf-brick, .prompt_area, pre, ul, ol, li';container.querySelectorAll(selectors).forEach(el => {
                if (el.querySelector(':scope > putdoc-toolbar')) return;
                ensurePositioned(el);
                const puid = ensurePuid(el);
                const toolbar = document.createElement('putdoc-toolbar');
                toolbar.setAttribute('snippet-id', snippetId);
                toolbar.setAttribute('puid', puid);
                toolbar.setAttribute('kind', (el.classList[0] || el.tagName.toLowerCase()));
                el.prepend(toolbar);
            });
        }

        function enhanceById(id) {
            const el = document.getElementById(id);
            if (!el) return;
            const sid = el.getAttribute('data-snippet-id') || '';
            enhance(el, sid);
        }

        // Watch for dynamic changes
        // putdocEnh.js
        function observe(container, snippetId) {
            if (!container) return;
            if (container._pdObserver) {           // already bound → just (re)enhance
                enhance(container, snippetId);
                return container._pdObserver;
            }
            const mo = new MutationObserver(() => enhance(container, snippetId));
            container._pdObserver = mo;            // cache on the element
            mo.observe(container, { childList: true, subtree: true });
            enhance(container, snippetId);
            return mo;
        }

        function observeById(id) {
            const el = document.getElementById(id);
            if (!el) return;
            const sid = el.getAttribute('data-snippet-id') || '';
            observe(el, sid);                       // will no-op if already observed
        }


        return {
            setHub,getHub,
            enhance, observe, enhanceById, observeById,
            copyByPuid: copyByPuidClean,
            clearSelected, markSelected
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

    // near your other helpers:
    window.putdocPresence = window.putdocPresence || {};
    window.putdocPresence.releaseCurrent = async function () {
        try {
            if (window.__pdToolbarHandlersInstalled && window.putdocEnh.getHub()) {
                await window.window.putdocEnh.__hub.invokeMethodAsync('ReleaseCurrent');
            } else if (window.putdocEnh && typeof window.putdocEnh.setHub === 'function') {
                // no-op fallback if hub isn't ready
            }
        } catch (e) { /* swallow */ }
    };

    window.putdocPresence.acquireSnippet = async function (snippetId, force) {
        try {
            const hub = window.window.putdocEnh.getHub();        // set in putdocEnh.setHub
            if (!hub) return { status: "error", message: "hub not ready" };

            let res = await hub.invokeMethodAsync("AcquireForEdit", "snippet", "", snippetId, !!force);
            if (res?.status === "denied") {
                const ok = confirm(`Snippet is locked by ${res.holder?.user ?? "someone"}. Take over?`);
                if (!ok) return res;
                res = await hub.invokeMethodAsync("AcquireForEdit", "snippet", "", snippetId, true);
            }
            return res;
        } catch (e) { return { status: "error", message: String(e) }; }
    };


})();
