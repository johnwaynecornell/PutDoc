(function () {
    function uuid() {
        return 'p' + (crypto.randomUUID ? crypto.randomUUID().replace(/-/g, '') : (Date.now() + Math.random()).toString(36));
    }

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

    window.putdoc.readClipboardHtmlOrNull = async function() {
        if (navigator.clipboard && navigator.clipboard.read) {
            try {
                const items = await navigator.clipboard.read();
                for (const item of items) {
                    if (item.types.includes('text/html')) {
                        const blob = await item.getType('text/html');
                        const html = await blob.text();
                        return html || null;
                    }
                }
            } catch {
                return null;
            }
        }
        return null;
    };

    // JavaScript
    window.putdocCombo = {
        init(input, options, dotnetRef /* optional: to push value */) {
            const list = document.createElement('div');
            list.className = 'pd-combo';
            Object.assign(list.style, { position:'absolute', display:'none', overflowY:'auto', zIndex:10000 });
            document.body.appendChild(list);

            let rafId = 0;
            function position() {
                cancelAnimationFrame(rafId);
                rafId = requestAnimationFrame(() => {
                    const r = input.getBoundingClientRect();
                    const vw = document.documentElement.clientWidth;
                    const vh = document.documentElement.clientHeight;
                    const minW = Math.min(r.width, vw * 0.9);
                    const belowSpace = vh - r.bottom;
                    const aboveSpace = r.top;
                    const desired = Math.min(320, vh * 0.5);
                    let maxH, top;
                    if (belowSpace >= 160 || belowSpace >= aboveSpace) {
                        maxH = Math.max(120, Math.min(desired, belowSpace - 8));
                        top = r.bottom + window.scrollY;
                    } else {
                        maxH = Math.max(120, Math.min(desired, aboveSpace - 8));
                        top = r.top + window.scrollY - maxH;
                    }
                    const left = Math.max(8, Math.min(r.left, vw - 8 - minW)) + window.scrollX;
                    list.style.minWidth = `${minW}px`;
                    list.style.maxHeight = `${maxH}px`;
                    list.style.left = `${left}px`;
                    list.style.top = `${top}px`;
                });
            }

            // Debounced filter only updates suggestions, not the input value
            let updId;
            function update() {
                clearTimeout(updId);
                const q = (input.value || '').toLowerCase();
                updId = setTimeout(() => {
                    const items = options.filter(v => v.toLowerCase().includes(q)).slice(0, 200);
                    render(items);
                }, 0);
            }

            function render(items) {
                list.innerHTML = '';
                if (!items.length) { list.style.display = 'none'; return; }
                for (const v of items) {
                    const it = document.createElement('div');
                    it.className = 'pd-combo-item';
                    it.textContent = v;
                    it.tabIndex = 0;
                    it.onclick = () => commit(v);
                    it.onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); commit(v); } };
                    list.appendChild(it);
                }
                list.style.display = 'block';
                position();
            }

            // Robust commit that survives re-renders
            function commit(v) {
                list.style.display = 'none';
                // Next microtask: after potential Blazor re-render swaps the input
                Promise.resolve().then(() => {
                    // Re-query the input by name to survive node replacement
                    const el = document.querySelector(`input[name="${input.name}"]`) || input;
                    el.value = v;
                    // Fire input + change with bubbles so @bind sees it
                    el.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
                    el.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
                    el.focus();
                });
            }

            input.addEventListener('input', update);
            input.addEventListener('focus', update);
            input.addEventListener('blur', () => setTimeout(() => (list.style.display = 'none'), 150));
            window.addEventListener('scroll', position, true);
            window.addEventListener('resize', position);

            return {
                dispose() {
                    clearTimeout(updId);
                    cancelAnimationFrame(rafId);
                    window.removeEventListener('scroll', position, true);
                    window.removeEventListener('resize', position);
                    list.remove();
                }
            };
        }
    };
    
    window.putdoc.toast = function (message) {
        const div = document.createElement("div");
        div.textContent = message;
        Object.assign(div.style, {
            position: "fixed", bottom: "1rem", right: "1rem",
            background: "#333", color: "#fff", padding: "0.5rem 1rem",
            borderRadius: "0.5rem", opacity: "0.9", zIndex: 9999,
            transition: "opacity 0.5s ease"
        });
        document.body.appendChild(div);
        setTimeout(() => { div.style.opacity = "0"; setTimeout(() => div.remove(), 500); }, 2500);
    }

    window.putdoc.getClientId = function () {
        try {
            let id = localStorage.getItem("pd.clientId");
            if (!id) {
                id = (crypto.randomUUID ? crypto.randomUUID() : (Date.now() + Math.random()).toString(36));
                localStorage.setItem("pd.clientId", id);
            }
            return id;
        } catch {
            return "anon-" + Math.random().toString(36).slice(2);
        }
    };

    window.putdoc.getSessionId = function () {
        try {
            let id = sessionStorage.getItem("pd.sessionId");
            if (!id) {
                id = (crypto.randomUUID ? crypto.randomUUID() : Math.random().toString(36).slice(2));
                sessionStorage.setItem("pd.sessionId", id);
            }
            return id;
        } catch {
            return "sess-" + Math.random().toString(36).slice(2);
        }
    };


    // putdoc.js
    window.putdocLayout = (function () {
        function clamp(v, min, max) {
            return Math.max(min, Math.min(max, v));
        }

        function px(n) {
            return `${Math.round(n)}px`;
        }

        function resolve(elOrSelector) {
            if (!elOrSelector) return null;
            if (typeof elOrSelector === 'string') return document.querySelector(elOrSelector);
            // If Blazor ElementReference is passed and is already a DOM node, just use it
            try {
                if (elOrSelector && elOrSelector.querySelector) return elOrSelector;
            } catch {
            }
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
            } catch {
            }

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
                    } catch {
                    }
                    document.removeEventListener('mousemove', onMove);
                    document.removeEventListener('mouseup', onUp);
                    document.removeEventListener('touchmove', onMove);
                    document.removeEventListener('touchend', onUp);
                    document.body.style.userSelect = prevSel;
                }

                document.addEventListener('mousemove', onMove);
                document.addEventListener('mouseup', onUp);
                document.addEventListener('touchmove', onMove, {passive: false});
                document.addEventListener('touchend', onUp);
            }

            if (vSplit && !vSplit._pdKeybound) {
                vSplit._pdKeybound = true;
                vSplit.addEventListener('mousedown', (e) => startDrag(e, 'x'));
                vSplit.addEventListener('touchstart', (e) => startDrag(e, 'x'), {passive: false});
                vSplit.addEventListener('keydown', (e) => {
                    const step = (e.shiftKey ? 40 : 16);
                    if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(el).getPropertyValue('--index-w')) || 420;
                        const delta = (e.key === 'ArrowLeft' ? -step : step);
                        const w = clamp(cur + delta, 240, Math.min(window.innerWidth * 0.6, el.getBoundingClientRect().width - 240));
                        el.style.setProperty('--index-w', px(w));
                        try {
                            localStorage.setItem('putdoc.indexW', px(w));
                        } catch {
                        }
                    }
                });
            }

            if (hSplit && !hSplit._pdKeybound) {
                hSplit._pdKeybound = true;
                hSplit.addEventListener('mousedown', (e) => startDrag(e, 'y'));
                hSplit.addEventListener('touchstart', (e) => startDrag(e, 'y'), {passive: false});
                hSplit.addEventListener('keydown', (e) => {
                    const step = (e.shiftKey ? 40 : 16);
                    if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
                        e.preventDefault();
                        const cur = parseFloat(getComputedStyle(el).getPropertyValue('--editor-h')) || 320;
                        const delta = (e.key === 'ArrowUp' ? -step : step);
                        const h = clamp(cur + delta, 200, Math.min(window.innerHeight * 0.75, el.getBoundingClientRect().height - 200));
                        el.style.setProperty('--editor-h', px(h));
                        try {
                            localStorage.setItem('putdoc.editorH', px(h));
                        } catch {
                        }
                    }
                });
            }
        }

        return {initSplitters};
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

        return {init};
    })();


    // Ensure namespace
    window.putdocText = window.putdocText || (window.putdocText = ({}));

    (function (ns) {
        // WeakMap: textarea -> { start, end }
        const caretCache = new WeakMap();

        function updateCache(ta) {
            try {
                caretCache.set(ta, {start: ta.selectionStart ?? 0, end: ta.selectionEnd ?? 0});
            } catch { /* noop */
            }
        }

        ns.initEditor = function (ta) {
            // keep your existing tab indent if available
            if (typeof ns.bindTabIndent === 'function') {
                try {
                    ns.bindTabIndent(ta);
                } catch {
                }
            }
            // seed + track caret on typical events
            updateCache(ta);
            // In putdocText module (augment the tracker you added earlier)
            ['beforeinput', 'input', 'keyup', 'mouseup', 'pointerup', 'select', 'focus'].forEach(evt =>
                ta.addEventListener(evt, () => updateCache(ta))
            );

            // if focus is regained, refresh
            ta.addEventListener('focus', () => updateCache(ta), {passive: true});
        };

        ns.getCachedSel = function (ta) {
            const s = caretCache.get(ta);
            if (s) return s;
            // fallback to live read
            try {
                return {start: ta.selectionStart ?? 0, end: ta.selectionEnd ?? 0};
            } catch {
                return {start: 0, end: 0};
            }
        };

        ns.setSelSmooth = function (ta, start, end) {
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    try {
                        ta.setSelectionRange(start, end);
                        ta.focus();
                    } catch {
                    }
                });
            });
        };

        // Assumes caretCache: WeakMap<HTMLTextAreaElement, {start:number, end:number}>
        ns.setSelAndCache = function (ta, start, end) {
            if (!ta) return Promise.resolve();

            // Clamp to current value length to avoid DOM exceptions
            const len = (ta.value && ta.value.length) || 0;
            start = Math.max(0, Math.min(start ?? 0, len));
            end = Math.max(0, Math.min(end ?? start, len));

            return new Promise((resolve) => {
                requestAnimationFrame(() => {
                    requestAnimationFrame(() => {
                        try {
                            ta.setSelectionRange(start, end);
                            ta.focus();
                        } catch {
                        }

                        // Update cache optimistically
                        try {
                            caretCache.set(ta, {start, end});
                        } catch {
                        }

                        // Verify; if it didn't take, try one corrective pass
                        try {
                            const curStart = ta.selectionStart ?? 0;
                            const curEnd = ta.selectionEnd ?? 0;
                            if (curStart !== start || curEnd !== end) {
                                requestAnimationFrame(() => {
                                    try {
                                        ta.setSelectionRange(start, end);
                                        ta.focus();
                                    } catch {
                                    }
                                    try {
                                        caretCache.set(ta, {start, end});
                                    } catch {
                                    }
                                    resolve();
                                });
                                return;
                            }
                        } catch {
                        }
                        resolve();
                    });
                });
            });
        };


    })(window.putdocText);

    // ... existing code ...
    (function () {
        // Utility: measure caret pixel position in a textarea
        function measureCaret(ta, pos) {
            const cs = getComputedStyle(ta);

            const mirror = document.createElement('div');
            Object.assign(mirror.style, {
                position: 'fixed',
                left: '-99999px',
                top: '0',
                visibility: 'hidden',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
                overflowWrap: 'break-word',
                boxSizing: 'content-box',
                width: ta.clientWidth + 'px',
                fontFamily: cs.fontFamily,
                fontSize: cs.fontSize,
                lineHeight: cs.lineHeight,
                letterSpacing: cs.letterSpacing,
                paddingTop: cs.paddingTop,
                paddingRight: cs.paddingRight,
                paddingBottom: cs.paddingBottom,
                paddingLeft: cs.paddingLeft
            });
            document.body.appendChild(mirror);

            const val = ta.value || '';
            const before = val.slice(0, pos).replace(/\n$/g, '\n\u200b');

            mirror.textContent = before;
            const probe = document.createElement('span');
            probe.textContent = '\u200b';
            mirror.appendChild(probe);

            const mirrorRect = mirror.getBoundingClientRect();
            const probeRect  = probe.getBoundingClientRect();

            // Offsets inside the mirror (content-box) in viewport coords
            const xInMirror = probeRect.left - mirrorRect.left;
            const yInMirror = probeRect.top  - mirrorRect.top;

            // Convert to textarea content coordinates by adding its internal scroll
            const x = Math.max(0, xInMirror - ta.scrollLeft);
            const y = Math.max(0, yInMirror - ta.scrollTop);

            const lh = parseFloat(cs.lineHeight) || probeRect.height || 18;

            mirror.remove();
            return { x, y, lineH: lh };
        }
        window.putdocText = window.putdocText || {};

        window.putdocText.flashCaretMarker = function(ta, duration = 800) {
            try {
                const pos = ta.selectionStart ?? 0;
                const { x, y, lineH } = measureCaret(ta, pos);
                const taRect = ta.getBoundingClientRect();

                // If caret line is not visible, skip
                const caretTopInViewport = taRect.top + y;
                const caretBottomInViewport = caretTopInViewport + lineH;
                const vh = document.documentElement.clientHeight;
                if (caretBottomInViewport < 0 || caretTopInViewport > vh) return;

                const marker = document.createElement('div');
                Object.assign(marker.style, {
                    position: 'fixed',
                    left: (taRect.left + x) + 'px',
                    top:  (taRect.top  + y) + 'px',
                    width: '2px',
                    height: lineH + 'px',
                    background: '#ff3b30',
                    borderRadius: '1px',
                    boxShadow: '0 0 0 2px rgba(255,59,48,0.2)',
                    opacity: '1',
                    pointerEvents: 'none',
                    zIndex: 2147483647,  // very on top
                    transition: 'opacity 0.6s ease'
                });
                document.body.appendChild(marker);
                const fadeAfter = Math.max(0, duration - 600);
                setTimeout(() => { marker.style.opacity = '0'; setTimeout(() => marker.remove(), 650); }, fadeAfter);
            } catch { /* ignore */ }
        };

        // Highlight current line briefly (viewport-fixed)
        window.putdocText.flashCaretLine = function(ta, duration = 900) {
            try {
                const pos = ta.selectionStart ?? 0;
                const { y, lineH } = measureCaret(ta, pos);
                const taRect = ta.getBoundingClientRect();

                const caretTopInViewport = taRect.top + y;
                const caretBottomInViewport = caretTopInViewport + lineH;
                const vh = document.documentElement.clientHeight;
                if (caretBottomInViewport < 0 || caretTopInViewport > vh) return;

                const width = taRect.width; // visible width in viewport
                const bar = document.createElement('div');
                Object.assign(bar.style, {
                    position: 'fixed',
                    left: taRect.left + 'px',
                    top:  (taRect.top + y) + 'px',
                    width: width + 'px',
                    height: lineH + 'px',
                    background: 'rgba(255, 235, 59, 0.25)',
                    outline: '1px solid rgba(255, 193, 7, 0.5)',
                    pointerEvents: 'none',
                    zIndex: 2147483646,
                    opacity: '1',
                    transition: 'opacity 0.5s ease'
                });
                document.body.appendChild(bar);
                const fadeAfter = Math.max(0, duration - 500);
                setTimeout(() => { bar.style.opacity = '0'; setTimeout(() => bar.remove(), 520); }, fadeAfter);
            } catch { /* ignore */ }
        };
    })();

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

    window.putdocText.indent = function (ta, outdent) {

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
        el.dispatchEvent(new Event('input', {bubbles: true}));
    };

    window.putdocText.getSel = (ta) => {
        return {
            start: ta.selectionStart ?? 0,
            end: ta.selectionEnd ?? 0 //ta.selectionStart ?? 0
        };
    };
    //window.putdocText.getSel = (ta) => [ta.selectionStart, ta.selectionEnd];
    window.putdocText.setSel = (ta, s, e) => {
        ta.selectionStart = s;
        ta.selectionEnd = e;
        ta.focus();
    };

    window.putdocText.insertAtCaret = (ta, text) => {
        const s = ta.selectionStart, e = ta.selectionEnd;
        ta.value = ta.value.slice(0, s) + text + ta.value.slice(e);
        const pos = s + text.length;
        ta.selectionStart = ta.selectionEnd = pos;
        ta.dispatchEvent(new Event('input', {bubbles: true}));
        ta.focus();
    };

    window.putdocText.wrapSelection = (ta, before, after) => {
        const s = ta.selectionStart, e = ta.selectionEnd;
        const val = ta.value;
        const selected = val.slice(s, e);

        ta.value = val.slice(0, s) + before + selected + after + val.slice(e);

        // keep selection on the original content
        const newStart = s + before.length;
        const newEnd = newStart + selected.length;
        ta.selectionStart = newStart;
        ta.selectionEnd = newEnd;

        ta.dispatchEvent(new Event('input', {bubbles: true}));
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
            }
        };

        ta.addEventListener('keydown', handler);
        ta._pdKeysCleanup = () => ta.removeEventListener('keydown', handler);
    };

    window.putdocText.unbindEditorShortcuts = function (ta) {
        if (!ta || !ta._pdKeysBound) return;
        try {
            ta._pdKeysCleanup && ta._pdKeysCleanup();
        } catch {
        }
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
                const end = ta.selectionEnd ?? start;
                dotnetRef && dotnetRef.invokeMethodAsync('NotifyCaretChanged', start, end);
            } catch {
            }
        };

        const onMove = () => {
            if (document.activeElement !== ta) return;
            if (rafId) cancelAnimationFrame(rafId);
            rafId = requestAnimationFrame(fire);
        };

        ['keyup', 'mouseup', 'select', 'focus', 'beforeinput', 'input'].forEach(ev =>
            ta.addEventListener(ev, onMove, {passive: true})
        );

        ta._pdCaretNotifyCleanup = () => {
            ['keyup', 'mouseup', 'select', 'focus', 'beforeinput', 'input'].forEach(ev =>
                ta.removeEventListener(ev, onMove)
            );
            if (rafId) cancelAnimationFrame(rafId);
            rafId = 0;
        };
    };

    window.putdocText.unbindCaretNotify = function (ta) {
        if (!ta || !ta._pdCaretNotifyBound) return;
        try {
            ta._pdCaretNotifyCleanup && ta._pdCaretNotifyCleanup();
        } catch {
        }
        ta._pdCaretNotifyBound = false;
        delete ta._pdCaretNotifyCleanup;
    };


    window.putdocEnh = (function () {
        let __hubRef = null; // DotNetObjectReference set by ToolbarHub once
        let __currentOpen = null;

        let __leaderId = null;

// claim leadership for this component id (string). returns true if you are leader
        function claimLeader(id) {
            if (!__leaderId) {
                __leaderId = id;
                return true;
            }
            return __leaderId === id;
        };

        // release leadership if held by this id
        function releaseLeader(id) {
            if (__leaderId === id) __leaderId = null;
        };

        // Set/get/clear the single shared hub ref
        function setHub(dotNetRef) {
            // only the current leader may replace the hub
            if (!__leaderId) return null; // no leader → ignore
            __hubRef = dotNetRef;
            return __hubRef;
        };

        function getHub() {
            return __hubRef;
        };

        function clearHub(dotNetRef) {
            if (__hubRef === dotNetRef) __hubRef = null;
        };

        function hasHub() {
            return __hubRef != null;
        };

        // Optional helper used earlier in your code
        function closeAllToolbars() {
            document.querySelectorAll('.pd-inline-toolbar[data-open="true"]')
                .forEach(shell => {
                    shell.dataset.open = 'false';
                    shell.setAttribute('aria-expanded', 'false');
                });
        };


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
                ta.value = html;
                document.body.appendChild(ta);
                ta.select();
                document.execCommand('copy');
                ta.remove();
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
            const gear = __currentOpen.querySelector('.pd-gear');
            if (shell) {
                shell.dataset.open = 'false';
                shell.setAttribute('aria-expanded', 'false');
            }
            if (refocus && gear) gear.focus();
            __currentOpen = null;
        }

        function closeAllToolbars() {
            closeCurrent(true);
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
                btn.addEventListener('click', async (ev) => {
                    ev.preventDefault();
                    ev.stopPropagation();

                    if (btn.disabled || btn.hasAttribute('disabled')) return;

                    const act = btn.getAttribute('data-act');
                    if (!__hubRef || !act) return;

                    const isRO = panelEl.getAttribute('data-readonly');

                    // For edit actions, try acquire → prompt override if denied
                    if (act === 'edit-inner' || act === 'edit-outer') {
                        const kind = (act === 'edit-outer') ? 'fragment-outer' : 'fragment-inner';
                        if (isRO) {
                            await __hubRef.invokeMethodAsync('OpenFragment', kind, puid, snippetId);
                            closeCurrent(false);
                            return;
                        } else {
                            let res = await __hubRef.invokeMethodAsync('AcquireForEdit', kind, puid, snippetId, /*force*/ false);
                            if (res?.status === 'denied') {
                                // simple prompt; replace with your nicer UI if you like
                                const ok = confirm(`Held by ${res.holder?.user ?? 'someone'}. Take over?`);
                                if (!ok) return;
                                res = await __hubRef.invokeMethodAsync('AcquireForEdit', kind, puid, snippetId, /*force*/ true);
                                if (res?.status !== 'granted' && res?.status !== 'stolen') return;
                            }
                        }
                    }

                    const out = await __hubRef.invokeMethodAsync('Handle', act, puid, snippetId);
                    closeCurrent(false);
                });
            });
        }


        class PutDocToolbar extends HTMLElement {
            connectedCallback() {
                if (this._wired) return;
                this._wired = true;

                const snippetId = this.getAttribute('snippet-id') || '';
                const puid = this.getAttribute('puid') || '';
                const kind = this.getAttribute('kind') || '';

                this.classList.add('pd-toolbar-host');
                this.innerHTML = `
      <span class="pd-inline-toolbar" data-open="false" aria-expanded="false" aria-haspopup="menu">
        <button type="button" class="btn drop pd-gear" title="Actions" aria-label="Open toolbar">▼</button>
        <div class="pd-toolbar-panel-slot"></div>
      </span>
    `;
                const shell = this.querySelector('.pd-inline-toolbar');
                const gear = this.querySelector('.pd-gear');
                const slot = this.querySelector('.pd-toolbar-panel-slot');

                gear?.addEventListener('click', async (e) => {
                    e.stopPropagation();
                    const isOpen = shell?.dataset.open === 'true';

                    if (!isOpen) {
                        requestAnimationFrame(() => {
                            gear.innerText = "▲";
                        });

                        // Always refetch fresh menu HTML — no caching
                        if (!__hubRef) return;
                        const html = await __hubRef.invokeMethodAsync('GetMenuHtml', kind, puid, snippetId);
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
        
        const PD_ENHANCED  = 'pdEnhanced';
        const PD_ENHANCING = 'pdEnhancing';

        function beginEnhancing(el) {
            if (el.dataset[PD_ENHANCED] === '1' || el.dataset[PD_ENHANCING] === '1') return false;
            el.dataset[PD_ENHANCING] = '1';
            return true;
        }
        function finishEnhancing(el) {
            el.dataset[PD_ENHANCED] = '1';
            delete el.dataset[PD_ENHANCING];
        }

        // one-time observer wrapper
        function observe(container, snippetId) {
            if (!container || container.__pdObserved) return;
            container.__pdObserved = true;

            const mo = new MutationObserver(() => {
                mo.disconnect();
                try { enhance(container, snippetId); }
                finally { mo.observe(container, { childList: true, subtree: true }); }
            });
            mo.observe(container, { childList: true, subtree: true });
            enhance(container, snippetId);
            return mo;
        }

        function withGuard(container, fn) {
            if (container._pdEnhancing) return;
            container._pdEnhancing = true;
            try { fn(); } finally { container._pdEnhancing = false; }
        }

        function preclean(container) {
            // Remove wrapper rows that lost their block child (root cause of “mystery toolbars”)
            container.querySelectorAll('.pd-row-wrap').forEach(wrap => {
                const hasBlock = wrap.querySelector(':scope > ul, :scope > ol, :scope > pre, :scope > svg');
                if (!hasBlock) wrap.remove();
            });

            // Remove stray toolbars sitting at the snippet root that no longer have a legit owner
            container.querySelectorAll(':scope > putdoc-toolbar').forEach(tb => {
                const puid = tb.dataset.ownerPuid || tb.getAttribute('puid') || '';
                if (!puid) { tb.remove(); return; }
                const stillHasHost =
                    container.querySelector(`.pd-row-wrap[data-for-puid="${puid}"]`) ||
                    container.querySelector(`li[data-puid="${puid}"]`) ||
                    container.querySelector(`[data-puid="${puid}"]`);
                if (!stillHasHost) tb.remove();
            });

            // Guard against duplicate LI gears: only one direct toolbar per <li>
            container.querySelectorAll('li').forEach(li => {
                const toolbars = li.querySelectorAll(':scope > putdoc-toolbar');
                for (let i = 1; i < toolbars.length; i++) toolbars[i].remove();
            });
        }

        // Find the Blazor scope attr name (e.g., "b-7r8g3f2w") on an ancestor
        function findBlazorScopeAttr(el) {
            let cur = el;
            while (cur && cur.nodeType === 1) {
                for (const a of cur.attributes) {
                    // Blazor scope attrs look like "b-xxxx..." and have empty value
                    if (a.name.startsWith('b-') && a.value === '') return a.name;
                }
                cur = cur.parentElement;
            }
            return null;
        }

// Apply that scope attr to any new node we inject
        function applyScopeAttr(newEl) {
            // const scope = __blazorScopeAttr;
            // if (scope) newEl.setAttribute(scope, '');
            // else console.warn('No Blazor scope attr missing', newEl);
        }

        var __blazorScopeAttr;
        function initMyScopedEnhancements(anchorEl) {
            // __blazorScopeAttr= findBlazorScopeAttr(anchorEl);
            // if (!__blazorScopeAttr) console.error('No Blazor scope attr found on', anchorEl);
        }
        
        function ensureToolbar(hostEl, snippetId, puid, kind) {
            if (hostEl.querySelector(':scope > putdoc-toolbar')) return;
            const tb = document.createElement('putdoc-toolbar');
            applyScopeAttr(tb);
            
            tb.className = 'pd-toolbar-host';
            tb.setAttribute('snippet-id', snippetId);
            tb.setAttribute('puid', puid);
            tb.dataset.ownerPuid = puid;
            tb.setAttribute('kind', kind);
            hostEl.prepend(tb);
        }

        
        
        function wrapBlockWithToolbar(blockEl, snippetId, puid) {
            const kind = blockEl.tagName.toLowerCase();

            // Already wrapped for this puid?
            const parent = blockEl.parentElement;
            if (parent && parent.classList.contains('pd-row-wrap') && parent.dataset.forPuid === puid) {
                ensureToolbar(parent, snippetId, puid, kind);
                return parent; // wrapper is host
            }
            
            // Fresh wrapper (atomic)
            const wrap = document.createElement('div');
            applyScopeAttr(wrap);
            wrap.className = 'pd-row-wrap';
            wrap.dataset.forPuid = puid;
            blockEl.replaceWith(wrap);
            wrap.appendChild(blockEl);

            ensureToolbar(wrap, snippetId, puid, kind);
            return wrap; // wrapper is host
        }
        
        
        function wrapBlockWithToolbar2(listEl, snippetId, puid) {
            const kind = listEl.tagName.toLowerCase();

            // Already wrapped for this puid?
            const parent = listEl.parentElement;
            if (parent && parent.classList.contains('pd-row-wrap') && parent.dataset.forPuid === puid) {
                ensureToolbar(parent, snippetId, puid, kind);
                return parent; // wrapper is host
            }

            // Fresh wrapper (atomic)
            const wrap = document.createElement('div');
            applyScopeAttr(wrap);
            wrap.className = 'pd-row-wrap';
            wrap.dataset.forPuid = puid;
            listEl.replaceWith(wrap);
            wrap.appendChild(listEl);

            ensureToolbar(wrap, snippetId, puid, kind);
            return wrap; // wrapper is host
        } 

        
        function ensureLiRow(li, snippetId, puid) {
            let row = li.querySelector(':scope > .pd-row');
            if (!row) {
                row = document.createElement('div');
                applyScopeAttr(row);
                
                row.className = 'pd-row';
                // move all children (except any existing toolbar) into a body
                const body = document.createElement('div'); 
                applyScopeAttr(body);
                
                body.className = 'pd-body';
                for (let n = li.firstChild; n; ) {
                    const next = n.nextSibling;
                    if (!(n.nodeType === 1 && n.tagName === 'PUTDOC-TOOLBAR')) body.appendChild(n);
                    n = next;
                }
                li.appendChild(row);
                row.appendChild(body);
            }
            ensureToolbar(li, snippetId, puid, 'li');
        }
        
        function enhance(container, snippetId) {
            
            if (!container) return;
            withGuard(container, () => {
                preclean(container);

                // Pass 1: whole-block hosts
                container.querySelectorAll('ul, ol, pre, svg').forEach(el => {
                    const puid = ensurePuid(el);
                    wrapBlockWithToolbar(el, snippetId, puid);
                });

                // Pass 2: list items
                container.querySelectorAll('li').forEach(li => {
                    const puid = ensurePuid(li);
                    ensureLiRow(li, snippetId, puid);
                });

                // Pass 3: other host types (card/brick/prompt)
                container.querySelectorAll('.slf-card, .slf-brick, .prompt_area').forEach(el => {
                    const puid = ensurePuid(el);
                    const kind = (el.classList[0] || el.tagName.toLowerCase());
                    ensureToolbar(el, snippetId, puid, kind);
                });
            });
        }



        function enhanceById(id) {
            const el = document.getElementById(id);
            if (!el) return;
            const sid = el.getAttribute('data-snippet-id') || '';
            enhance(el, sid);
        }
        
        function observeById(id) {
            const el = document.getElementById(id);
            if (!el) return;
            const sid = el.getAttribute('data-snippet-id') || '';
            observe(el, sid);                       // will no-op if already observed
        }


        return {
            claimLeader, releaseLeader,
            setHub, getHub, clearHub, hasHub,
            enhance, observe, enhanceById, observeById, initMyScopedEnhancements,
            copyByPuid: copyByPuidClean,
            clearSelected, markSelected,
            closeAllToolbars
        };
    })();
    
    (function () {
        let hubResolve, hubReady = false;
        const hubPromise = new Promise(res => hubResolve = res);

        // expose
        window.putdocEnh = window.putdocEnh || {};
        const oldSetHub = window.putdocEnh.setHub;

        window.putdocEnh.setHub = function (dotnetRef) {
            if (typeof oldSetHub === "function") oldSetHub(dotnetRef);
            // mark ready once
            if (!hubReady) { hubReady = true; try { hubResolve(true); } catch {} }
        };

        // awaitable helper with timeout
        window.putdocWaitForHub = function (timeoutMs = 3000) {
            if (hubReady) return Promise.resolve(true);
            let timer;
            return Promise.race([
                hubPromise,
                new Promise(res => timer = setTimeout(() => res(false), timeoutMs))
            ]).finally(() => { if (timer) clearTimeout(timer); });
        };

        // cheap “is it ready” check (kept for diagnostics)
        window.putdocEnh.hasHub = function () { return !!hubReady; };
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
                    console.log('Blazor.start succeeded');
                }
            } catch (e) {
                console.error('Blazor.start failed', e);
            }

            // Your initializers (safe if they’re idempotent)
            //try { window.putdocHeader?.init(); } catch {}
            try {
                const layout = document.getElementById('putdocLayout');
                if (layout) window.putdocLayout?.initSplitters(layout);
            } catch {
            }
        });
    })();

    // near your other helpers:
    window.putdocPresence = window.putdocPresence || {};
    window.putdocPresence.releaseCurrent = async function () {
        try {
            if (window.__pdToolbarHandlersInstalled && window.putdocEnh.getHub()) {
                await window.window.putdocEnh.__hubRef.invokeMethodAsync('ReleaseCurrent');
            } else if (window.putdocEnh && typeof window.putdocEnh.setHub === 'function') {
                // no-op fallback if hub isn't ready
            }
        } catch (e) { /* swallow */
        }
    };

    window.putdocPresence.acquireSnippet = async function (snippetId, force) {
        try {
            const hub = window.window.putdocEnh.getHub();        // set in putdocEnh.setHub
            if (!hub) return {status: "error", message: "hub not ready"};

            let res = await hub.invokeMethodAsync("AcquireForEdit", "snippet", "", snippetId, !!force);
            if (res?.status === "denied") {
                const ok = confirm(`Snippet is locked by ${res.holder?.user ?? "someone"}. Take over?`);
                if (!ok) return res;
                res = await hub.invokeMethodAsync("AcquireForEdit", "snippet", "", snippetId, true);
            }
            return res;
        } catch (e) {
            return {status: "error", message: String(e)};
        }
    };

    window.putdocPresence.acquireWriter = async function (force) {
        try {
            return await window.putdocEnh.getHub()?.invokeMethodAsync('AcquireDocWriter', !!force);
        } catch {
            return {status: "error", message: "could not invoke AcquireDocWriter"};
        }
    }

    window.putdocPresence.releaseWriter = async function () {
        try {
            await window.putdocEnh.getHub()?.invokeMethodAsync('ReleaseDocWriter');
        } catch {
        }
    }

    window.putdocNav = {
        bindBeforeUnload(getDirtyOrFrozen) {
            const h = (e) => {
                try {
                    if (getDirtyOrFrozen()) {
                        e.preventDefault();
                        e.returnValue = ''; // required for Chrome
                        return '';
                    }
                } catch {
                }
            };
            window.addEventListener('beforeunload', h);
            return () => window.removeEventListener('beforeunload', h);
        }
    };

    // put this in putdoc.js once
    window.putdocAcquireWriter = async function (force) {
        const hub = window.putdocEnh?.getHub?.();
        if (!hub) return {status: "readonly", note: "no hub yet"};
        try {
            return await hub.invokeMethodAsync("AcquireDocWriter", !!force);
        } catch {
            return {status: "error"};
        }
    };

    window.putdocReleaseWriter = async function () {
        const hub = window.putdocEnh?.getHub?.();
        if (!hub) return {status: "ok", note: "no hub"};
        try {
            return await hub.invokeMethodAsync("ReleaseDocWriter");
        } catch {
            return {status: "error"};
        }
    };

    window.getTimeStamp = function ()
    {
        return "putdoc.js [2025-11-10-D]";
    }
    
    console.log(window.getTimeStamp() + " loaded");
})();
