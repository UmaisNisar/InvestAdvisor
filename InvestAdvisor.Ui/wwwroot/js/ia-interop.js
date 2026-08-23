// InvestAdvisor browser interop shared by both hosts (Blazor Server's App.razor and the MAUI
// BlazorWebView's index.html). Every function is called from a Razor component through
// IJSRuntime inside a try/catch, so a missing script degrades silently — which is exactly why
// it lives in the shared RCL rather than in one host.

window.iaRail = {
    isDesktop: () => window.matchMedia('(min-width: 960px)').matches,
    get: () => localStorage.getItem('ia-rail'),
    set: (v) => localStorage.setItem('ia-rail', v)
};
// Boot splash control — Root hides it once the circuit is interactive.
window.iaSplash = {
    hide: () => {
        var el = document.getElementById('ia-splash');
        if (el) { el.classList.add('ia-hide'); setTimeout(() => el.remove(), 400); }
    }
};
// Busy cursor: the nav loading bar toggles this so the pointer shows progress during loads.
window.iaBusy = {
    on: () => document.documentElement.classList.add('ia-busy'),
    off: () => document.documentElement.classList.remove('ia-busy')
};
// Animated count-up (Wealthsimple-style), used by the hero number and AnimatedNumber.
// Parses the element's current text as the start value so it tweens between renders,
// and degrades to the server-rendered static text when JS or rAF is unavailable.
window.iaCountUp = {
    // Generic tween: formats per opts { prefix, suffix, decimals, signed }.
    // signed = always show a leading + on positives (P/L-style figures).
    tween: (el, value, opts) => {
        if (!el) return;
        opts = opts || {};
        const d = Number.isInteger(opts.decimals) ? opts.decimals : 2;
        const fmt = v => (v < 0 ? '-' : (opts.signed ? '+' : ''))
            + (opts.prefix || '')
            + Math.abs(v).toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d })
            + (opts.suffix || '');
        // Mutate the existing text node's value rather than assigning textContent:
        // textContent would replace the node, detaching the one Blazor's diff holds a
        // reference to — its later patches (e.g. masking the value) would then hit a
        // dead node and never show up.
        const set = v => {
            const n = el.firstChild;
            if (n && n.nodeType === Node.TEXT_NODE) n.nodeValue = fmt(v);
            else el.textContent = fmt(v);
        };
        // Generation counter cancels a still-running tween when a newer value arrives,
        // so rapid refreshes don't leave two rAF loops fighting over the text.
        const gen = (el._iaGen = (el._iaGen || 0) + 1);
        const start = parseFloat((el.textContent || '').replace(/[^0-9.\-]/g, ''));
        if (!window.requestAnimationFrame || !isFinite(start) || start === value
            || (window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches)) { set(value); return; }
        const t0 = performance.now(), dur = 650;
        const step = t => {
            if (el._iaGen !== gen) return;
            const p = Math.min(1, (t - t0) / dur), e = 1 - Math.pow(1 - p, 3); // ease-out cubic
            set(start + (value - start) * e);
            if (p < 1) requestAnimationFrame(step);
        };
        requestAnimationFrame(step);
    },
    // Hero-compatible wrapper (dashboard portfolio value).
    to: (el, value) => window.iaCountUp.tween(el, value, { prefix: '$', decimals: 2 })
};
// Mobile haptics. navigator.vibrate is Android/Chromium only (iOS Safari ignores it).
// Honors a localStorage opt-out ('ia-haptics' = 'off'). Patterns are deliberately subtle.
window.iaHaptics = {
    enabled: () => 'vibrate' in navigator && localStorage.getItem('ia-haptics') !== 'off',
    tap: () => { if (window.iaHaptics.enabled()) navigator.vibrate(10); },
    success: () => { if (window.iaHaptics.enabled()) navigator.vibrate([12, 40, 12]); },
    warn: () => { if (window.iaHaptics.enabled()) navigator.vibrate(28); }
};
// Global search hotkey: Ctrl/Cmd+K opens the command palette (MainLayout registers itself).
window.iaHotkeys = {
    _ref: null,
    register: function (ref) {
        this._ref = ref;
        if (this._bound) return; this._bound = true;
        document.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
                e.preventDefault();
                if (window.iaHotkeys._ref) window.iaHotkeys._ref.invokeMethodAsync('OpenSearchFromHotkey');
            }
        });
    },
    unregister: function () { this._ref = null; }
};
// Pull-to-refresh (touch only). A page opts in by registering a .NET ref exposing an
// [JSInvokable] OnPullRefresh(); the gesture only engages at the top of the page. The
// body locks native overscroll (app.css), so this is the only pull affordance on phones.
window.iaPullRefresh = {
    _ref: null,
    _inited: false,
    register: function (ref) { this._ref = ref; this._init(); },
    unregister: function (ref) { if (this._ref === ref) this._ref = null; },
    _init: function () {
        if (this._inited) return; this._inited = true;
        var ind = document.createElement('div');
        ind.id = 'ia-ptr';
        ind.innerHTML = '<div class="ia-ptr-spinner"></div>';
        document.body.appendChild(ind);
        var startY = 0, dist = 0, pulling = false, busy = false;
        var THRESH = 70, MAX = 110;
        var self = this;
        document.addEventListener('touchstart', function (e) {
            if (busy || !self._ref || window.scrollY > 0 || e.touches.length !== 1) { pulling = false; return; }
            startY = e.touches[0].clientY; dist = 0; pulling = true;
        }, { passive: true });
        document.addEventListener('touchmove', function (e) {
            if (!pulling) return;
            dist = e.touches[0].clientY - startY;
            if (dist <= 0) { ind.style.transform = 'translateX(-50%) translateY(0)'; ind.style.opacity = '0'; return; }
            var d = Math.min(dist, MAX);
            ind.style.transform = 'translateX(-50%) translateY(' + d + 'px)';
            ind.style.opacity = Math.min(1, d / THRESH).toString();
            ind.classList.toggle('ia-ptr-ready', dist >= THRESH);
        }, { passive: true });
        document.addEventListener('touchend', async function () {
            if (!pulling) return;
            pulling = false;
            if (dist >= THRESH && self._ref && !busy) {
                busy = true;
                ind.classList.add('ia-ptr-spin');
                ind.style.transform = 'translateX(-50%) translateY(' + THRESH + 'px)';
                ind.style.opacity = '1';
                try { if (window.iaHaptics) window.iaHaptics.tap(); } catch (e) {}
                try { await self._ref.invokeMethodAsync('OnPullRefresh'); } catch (e) {}
                busy = false;
                ind.classList.remove('ia-ptr-spin', 'ia-ptr-ready');
            }
            ind.style.transform = 'translateX(-50%) translateY(0)';
            ind.style.opacity = '0';
        });
    }
};
// Global: a light tap pulse on any actionable element, touch input only (mouse never buzzes).
document.addEventListener('pointerup', function (e) {
    if (e.pointerType !== 'touch') return;
    if (e.target && e.target.closest('button, a[href], .mud-button-root, .mud-icon-button, .mud-nav-link, [role="button"], .mud-list-item-clickable'))
        window.iaHaptics.tap();
}, true);
// Global: success/warning buzz when a snackbar appears, keyed off MudBlazor's severity class.
new MutationObserver(function (muts) {
    for (var m of muts) for (var n of m.addedNodes) {
        if (!(n instanceof HTMLElement)) continue;
        var alert = n.matches && n.matches('.mud-snackbar') ? n : n.querySelector && n.querySelector('.mud-snackbar');
        if (!alert) continue;
        var c = alert.className || '';
        if (/error|warning/i.test(c)) window.iaHaptics.warn();
        else if (/success/i.test(c)) window.iaHaptics.success();
    }
}).observe(document.body, { childList: true, subtree: true });
