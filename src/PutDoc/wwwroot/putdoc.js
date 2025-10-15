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


    // putdoc.js
    window.putdocLayout = (function () {
        function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }
        function px(n) { return `${Math.round(n)}px`; }

        function resolve(elOrSelector) {
            if (!elOrSelector) return null;
            if (typeof elOrSelector === 'string') return document.querySelector(elOrSelector);
            // If Blazor ElementReference is passed and is already a DOM node, just use it
            try { if (elOrSelector && elOrSelector.querySelector) return elOrSelector; } catch {}
            return null;
        }

        function initSplitters(elOrSelector) {
            const el = resolve(elOrSelector);
            if (!el) {
                // Retry soon; caller may be ahead of the DOM
                requestAnimationFrame(() => {
                    const retry = resolve(elOrSelector);
                    if (retry) initSplitters(retry);
                    // else: quietly give up; caller can invoke again later (idempotent)
                });
                return;
            }
            if (el._splittersBound) return;
            el._splittersBound = true;

            // Restore sizes
            try {
                const savedW = localStorage.getItem('putdoc.indexW');
                const savedH = localStorage.getItem('putdoc.editorH');
                if (savedW) el.style.setProperty('--index-w', savedW);
                if (savedH) el.style.setProperty('--editor-h', savedH);
            } catch {}

            const vSplit = el.querySelector('[data-role="v-split"]');
            const hSplit = el.querySelector('[data-role="h-split"]');

            function startDrag(e, axis) {
                e.preventDefault();
                const rect = el.getBoundingClientRect();
                const startX = (e.touches ? e.touches[0].clientX : e.clientX);
                const startY = (e.touches ? e.touches[0].clientY : e.clientY);
                const startW = parseFloat(getComputedStyle(el).getPropertyValue('--index-w')) || 420;
                const startH = parseFloat(getComputedStyle(el).getPropertyValue('--editor-h')) || 320;

                const prevSel = document.body.style.userSelect;
                document.body.style.userSelect = 'none';

                function onMove(ev) {
                    const x = (ev.touches ? ev.touches[0].clientX : ev.clientX);
                    const y = (ev.touches ? ev.touches[0].clientY : ev.clientY);
                    if (axis === 'x') {
                        const dx = x - rect.left;
                        const w = clamp(dx, 240, Math.min(window.innerWidth * 0.6, rect.width - 240));
                        el.style.setProperty('--index-w', px(w));
                    } else {
                        const dy = y - rect.top;
                        const h = clamp(dy, 200, Math.min(window.innerHeight * 0.75, rect.height - 200));
                        el.style.setProperty('--editor-h', px(h));
                    }
                }
                function onUp() {
                    try {
                        const iw = getComputedStyle(el).getPropertyValue('--index-w').trim();
                        const eh = getComputedStyle(el).getPropertyValue('--editor-h').trim();
                        localStorage.setItem('putdoc.indexW', iw);
                        localStorage.setItem('putdoc.editorH', eh);
                    } catch {}
                    document.removeEventListener('mousemove', onMove);
                    document.removeEventListener('mouseup', onUp);
                    document.removeEventListener('touchmove', onMove);
                    document.removeEventListener('touchend', onUp);
                    document.body.style.userSelect = prevSel;
                }

                document.addEventListener('mousemove', onMove);
                document.addEventListener('mouseup', onUp);
                document.addEventListener('touchmove', onMove, { passive: false });
                document.addEventListener('touchend', onUp);
            }

            if (vSplit && !vSplit._pdKeybound) {
                vSplit._pdKeybound = true;
                vSplit.addEventListener('mousedown', (e) => startDrag(e, 'x'));
                vSplit.addEventListener('touchstart', (e) => startDrag(e, 'x'), { passive: false });
                vSplit.addEventListener('keydown', (e) => {
                    const step = (e.shiftKey ? 40 : 16);
                    if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(el).getPropertyValue('--index-w')) || 420;
                        const delta = (e.key === 'ArrowLeft' ? -step : step);
                        const w = clamp(cur + delta, 240, Math.min(window.innerWidth * 0.6, el.getBoundingClientRect().width - 240));
                        el.style.setProperty('--index-w', px(w));
                        try { localStorage.setItem('putdoc.indexW', px(w)); } catch {}
                    }
                });
            }

            if (hSplit && !hSplit._pdKeybound) {
                hSplit._pdKeybound = true;
                hSplit.addEventListener('mousedown', (e) => startDrag(e, 'y'));
                hSplit.addEventListener('touchstart', (e) => startDrag(e, 'y'), { passive: false });
                hSplit.addEventListener('keydown', (e) => {
                    const step = (e.shiftKey ? 40 : 16);
                    if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(el).getPropertyValue('--editor-h')) || 320;
                        const delta = (e.key === 'ArrowUp' ? -step : step);
                        const h = clamp(cur + delta, 200, Math.min(window.innerHeight * 0.75, el.getBoundingClientRect().height - 200));
                        el.style.setProperty('--editor-h', px(h));
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
            //const h = 0;
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

        // Assumes caretCache: WeakMap<HTMLTextAreaElement, {start:number, end:number}>
        ns.setSelAndCache = function (ta, start, end) {
            if (!ta) return Promise.resolve();

            // Clamp to current value length to avoid DOM exceptions
            const len = (ta.value && ta.value.length) || 0;
            start = Math.max(0, Math.min(start ?? 0, len));
            end   = Math.max(0, Math.min(end   ?? start, len));
             
            return new Promise((resolve) => {
                requestAnimationFrame(() => {
                    requestAnimationFrame(() => {
                        try {
                            ta.setSelectionRange(start, end);
                            ta.focus();
                        } catch { }

                        // Update cache optimistically
                        try { caretCache.set(ta, { start, end }); } catch {}

                        // Verify; if it didn't take, try one corrective pass
                        try {
                            const curStart = ta.selectionStart ?? 0;
                            const curEnd   = ta.selectionEnd   ?? 0;
                            if (curStart !== start || curEnd !== end) {
                                requestAnimationFrame(() => {
                                    try {
                                        ta.setSelectionRange(start, end);
                                        ta.focus();
                                    } catch {}
                                    try { caretCache.set(ta, { start, end }); } catch {}
                                    resolve();
                                });
                                return;
                            }
                        } catch { }
                        resolve();
                    });
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

    window.putdocText.wrapSelection = (ta, before, after) => {
        const s = ta.selectionStart, e = ta.selectionEnd;
        const val = ta.value;
        const selected = val.slice(s, e);

        ta.value = val.slice(0, s) + before + selected + after + val.slice(e);

        // keep selection on the original content
        const newStart = s + before.length;
        const newEnd   = newStart + selected.length;
        ta.selectionStart = newStart;
        ta.selectionEnd   = newEnd;

        ta.dispatchEvent(new Event('input', { bubbles: true }));
        ta.focus();
    };

    // Keyboard shortcuts scoped to a specific textarea
    window.putdocText.bindEditorShortcuts = function (ta, dotnetRef) {
        if (!ta || ta._pdKeysBound) return;
        ta._pdKeysBound = true;

        const handler = (e) => {
            // Only when textarea itself is focused
            if (document.activeElement !== ta) return;

            const ctrl = e.ctrlKey || e.metaKey; // support Cmd on macOS
            if (!ctrl) return;

            const key = (e.key || '').toLowerCase();

            if (key === 'z') {
                e.preventDefault(); // block native undo
                if (e.shiftKey) {
                    dotnetRef.invokeMethodAsync('InvokeRedo');
                } else {
                    dotnetRef.invokeMethodAsync('InvokeUndo');
                }
            } else if (key === 'y') {
                e.preventDefault(); // block native redo
                dotnetRef.invokeMethodAsync('InvokeRedo');
            } else if (key === 's') {
                e.preventDefault(); // stop browser's Save dialog
                dotnetRef.invokeMethodAsync('InvokeSaveNow');
            }
        };

        ta.addEventListener('keydown', handler);
        ta._pdKeysCleanup = () => ta.removeEventListener('keydown', handler);
    };

    window.putdocText.unbindEditorShortcuts = function (ta) {
        if (!ta || !ta._pdKeysBound) return;
        try { ta._pdKeysCleanup && ta._pdKeysCleanup(); } catch {}
        ta._pdKeysBound = false;
        delete ta._pdKeysCleanup;
    };


    // Keyboard shortcuts scoped to a specific textarea
    window.putdocText.bindEditorShortcuts = function (ta, dotnetRef) {
        if (!ta || ta._pdKeysBound) return;
        ta._pdKeysBound = true;

        const handler = (e) => {
            // Only when textarea itself is focused
            if (document.activeElement !== ta) return;

            const ctrl = e.ctrlKey || e.metaKey; // support Cmd on macOS
            if (!ctrl) return;

            const key = (e.key || '').toLowerCase();

            if (key === 'z') {
                e.preventDefault(); // block native undo
                if (e.shiftKey) {
                    dotnetRef.invokeMethodAsync('InvokeRedo');
                } else {
                    dotnetRef.invokeMethodAsync('InvokeUndo');
                }
            } else if (key === 'y') {
                e.preventDefault(); // block native redo
                dotnetRef.invokeMethodAsync('InvokeRedo');
            }
        };

        ta.addEventListener('keydown', handler);
        ta._pdKeysCleanup = () => ta.removeEventListener('keydown', handler);
    };

    window.putdocText.unbindEditorShortcuts = function (ta) {
        if (!ta || !ta._pdKeysBound) return;
        try { ta._pdKeysCleanup && ta._pdKeysCleanup(); } catch {}
        ta._pdKeysBound = false;
        delete ta._pdKeysCleanup;
    };

    window.putdocText.bindCaretNotify = function (ta, dotnetRef) {
        if (!ta || ta._pdCaretNotifyBound) return;
        ta._pdCaretNotifyBound = true;

        let rafId = 0;
        const fire = () => {
            rafId = 0;
            try {
                const start = ta.selectionStart ?? 0;
                const end   = ta.selectionEnd   ?? start;
                dotnetRef && dotnetRef.invokeMethodAsync('NotifyCaretChanged', start, end);
            } catch {}
        };

        const onMove = () => {
            if (document.activeElement !== ta) return;
            if (rafId) cancelAnimationFrame(rafId);
            rafId = requestAnimationFrame(fire);
        };

        ['keyup','mouseup','select','focus','beforeinput','input'].forEach(ev =>
            ta.addEventListener(ev, onMove, { passive: true })
        );

        ta._pdCaretNotifyCleanup = () => {
            ['keyup','mouseup','select','focus','beforeinput','input'].forEach(ev =>
                ta.removeEventListener(ev, onMove)
            );
            if (rafId) cancelAnimationFrame(rafId);
            rafId = 0;
        };
    };

    window.putdocText.unbindCaretNotify = function (ta) {
        if (!ta || !ta._pdCaretNotifyBound) return;
        try { ta._pdCaretNotifyCleanup && ta._pdCaretNotifyCleanup(); } catch {}
        ta._pdCaretNotifyBound = false;
        delete ta._pdCaretNotifyCleanup;
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

        function closeAllToolbars() { closeCurrent(true); }
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
                btn.addEventListener('click', async (ev) => {
                    ev.preventDefault();
                    ev.stopPropagation();

                    if (btn.disabled || btn.hasAttribute('disabled')) return;

                    const act = btn.getAttribute('data-act');
                    if (!__hub || !act) return;

                    const isRO = panelEl.getAttribute('data-readonly');
                    
                    // For edit actions, try acquire → prompt override if denied
                    if (act === 'edit-inner' || act === 'edit-outer') {
                        const kind = (act === 'edit-outer') ? 'fragment-outer' : 'fragment-inner';
                        if (isRO) {
                            await __hub.invokeMethodAsync('OpenFragment', kind, puid, snippetId);
                            closeCurrent(false);
                            return;
                        } else {
                            let res = await __hub.invokeMethodAsync('AcquireForEdit', kind, puid, snippetId, /*force*/ false);
                            if (res?.status === 'denied') {
                                // simple prompt; replace with your nicer UI if you like
                                const ok = confirm(`Held by ${res.holder?.user ?? 'someone'}. Take over?`);
                                if (!ok) return;
                                res = await __hub.invokeMethodAsync('AcquireForEdit', kind, puid, snippetId, /*force*/ true);
                                if (res?.status !== 'granted' && res?.status !== 'stolen') return;
                            }
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
        <button type="button" class="btn drop pd-gear" title="Actions" aria-label="Open toolbar">▼</button>
        <div class="pd-toolbar-panel-slot"></div>
      </span>
    `;
                const shell = this.querySelector('.pd-inline-toolbar');
                const gear  = this.querySelector('.pd-gear');
                const slot  = this.querySelector('.pd-toolbar-panel-slot');

                gear?.addEventListener('click', async (e) => {
                    e.stopPropagation();
                    const isOpen = shell?.dataset.open === 'true';                     

                    if (!isOpen) {
                        requestAnimationFrame(() => {
                            gear.innerText = "▲";
                        });
                        
                        // Always refetch fresh menu HTML — no caching
                        if (!__hub) return;
                        const html = await __hub.invokeMethodAsync('GetMenuHtml', kind, puid, snippetId);
                        slot.innerHTML = html;

                        const root = slot.firstElementChild;
                        if (root) {
                            root.classList.add('menu-popover', 'pd-toolbar-panel');
                            wirePanelActions(root, puid, snippetId); // RO-aware action wiring
                        }
                        openThis(this);
                    } else {
                        requestAnimationFrame(() => {
                            gear.innerText = "▼";
                        });

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
            clearSelected, markSelected, 
            closeAllToolbars
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

    window.putdocNav = {
        bindBeforeUnload(getDirtyOrFrozen) {
            const h = (e) => {
                try {
                    if (getDirtyOrFrozen()) {
                        e.preventDefault();
                        e.returnValue = ''; // required for Chrome
                        return '';
                    }
                } catch {}
            };
            window.addEventListener('beforeunload', h);
            return () => window.removeEventListener('beforeunload', h);
        }
    };



})();
