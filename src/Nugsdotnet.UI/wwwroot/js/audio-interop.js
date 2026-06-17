// JS interop for the dual <audio> elements (gapless playback) + window-level
// keyboard shortcuts. Two elements ping-pong: one is active (audible), the
// other idle (preloading the next track). The active element's identity lives
// here in _els/_active — never inferred from the DOM (both share class
// "audio", which is used only for the CSS hide rule, never for resolution).
window.audioInterop = {
    _els: null,          // [elA, elB]
    _active: 0,          // index of the audible element
    _dotnetRef: null,
    _rebufferCount: 0,
    _statsEl: null,
    _statsListeners: null,
    _onEnded: null,
    _onError: null,

    _activeEl: function () { return this._els ? this._els[this._active] : null; },
    _idleEl: function () { return this._els ? this._els[1 - this._active] : null; },

    // ---- lifecycle -------------------------------------------------------
    init: function (elA, elB, dotnetRef) {
        if (this._els) return;                 // idempotent across reconnects
        this._els = [elA, elB];
        this._active = 0;
        this._dotnetRef = dotnetRef;
        this._rebufferCount = 0;

        const self = this;
        this._onEnded = function (e) {
            if (e.target !== self._activeEl()) return;   // a now-idle element's own 'ended' — ignore
            // Gapless hot-path: swap to the preloaded element NOW, in this same
            // 'ended' tick, with no JS→.NET→JS round-trip on the audio path. Then
            // tell .NET to sync the queue cursor. If preload wasn't ready (or the
            // queue ended), fall back to the .NET-driven advance.
            const sw = self._doSwap();
            if (sw) {
                if (typeof sw.catch === 'function') sw.catch(function (err) { console.warn('advance play() rejected:', err); });
                if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnAdvancedViaPreload').catch(function () { });
            } else if (self._dotnetRef) {
                self._dotnetRef.invokeMethodAsync('OnTrackEnded').catch(function () { });
            }
        };
        this._onError = function (e) {
            if (e.target === self._activeEl()) {
                if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnAudioError').catch(function () { });
            } else {
                // Idle/preload element failed (e.g. the next track is HLS → 415,
                // or no stream → 404). Swallow it; the next advance falls back
                // to a cold load that surfaces the error on the active element.
                e.target._preloadFailed = true;
            }
        };
        for (const el of this._els) {
            el.addEventListener('ended', this._onEnded);
            el.addEventListener('error', this._onError);
        }
        this._bindStats(this._activeEl());
    },

    dispose: function () {
        if (this._els && this._onEnded) {
            for (const el of this._els) {
                el.removeEventListener('ended', this._onEnded);
                el.removeEventListener('error', this._onError);
            }
        }
        this._unbindStats();
        this._onEnded = this._onError = null;
        this._dotnetRef = null;
        this._els = null;
    },

    // ---- playback control (ACTIVE element unless noted) ------------------
    playTrack: function (url) {
        const el = this._activeEl();
        if (!el) return;
        this._rebufferCount = 0;               // new track — reset rebuffer counter
        el._preloadFailed = false;
        el.src = url;
        const p = el.play();
        if (p && typeof p.catch === 'function') p.catch(err => console.warn('play() rejected:', err));
        this._bindStats(el);
    },

    // Point the IDLE element at the next track so it buffers ahead. Clears it
    // when url is null. Targets _idleEl() by index, so it can never disturb the
    // audible element.
    preloadNext: function (url) {
        const idle = this._idleEl();
        if (!idle) return;
        if (!url) {
            idle.removeAttribute('src');
            idle._preloadFailed = false;
            try { idle.load(); } catch (e) { }
            return;
        }
        idle._preloadFailed = false;
        idle.preload = 'auto';
        idle.src = url;
        try { idle.load(); } catch (e) { }
        // Nudge Chromium/WebView2 to actually buffer (preload=auto can otherwise
        // sit at metadata until currentTime is touched).
        const nudge = function () {
            try { idle.currentTime = 0; } catch (e) { }
            idle.removeEventListener('loadedmetadata', nudge);
        };
        idle.addEventListener('loadedmetadata', nudge);
    },

    // Core swap: the idle (preloaded) element becomes active and starts playing,
    // immediately and synchronously. Returns the play() promise on success, or
    // false if the preload wasn't ready. Shared by the JS hot-path (natural end,
    // see _onEnded) and the .NET-driven path (manual Next / cold-load fallback).
    _doSwap: function () {
        const incoming = this._idleEl();
        const outgoing = this._activeEl();
        if (!incoming || !incoming.src || incoming._preloadFailed) return false;
        if (!this._isReady(incoming)) return false;

        // Flip FIRST so the outgoing element is now the IDLE one: any 'error'
        // from tearing it down below is treated as an idle-element event and
        // swallowed.
        this._active = 1 - this._active;
        this._rebufferCount = 0;

        // Start the incoming element IMMEDIATELY — before binding telemetry or
        // touching the outgoing element — so nothing on the main thread delays or
        // glitches the new audio's first sample. The outgoing teardown happens
        // only after play() has been kicked off.
        const p = incoming.play();
        this._bindStats(incoming);           // telemetry follows the audible element
        if (outgoing) {
            try { outgoing.pause(); } catch (e) { }
            outgoing.removeAttribute('src');
            try { outgoing.load(); } catch (e) { }
        }
        return (p && typeof p.then === 'function') ? p : Promise.resolve();
    },

    // .NET-driven advance (manual Next / cold-load fallback). Awaits play() so a
    // rejection reports false, and watches briefly for an immediate stall.
    // Returns false if the preload wasn't ready / play() failed, so .NET can
    // cold-load instead. (Natural-end advance does NOT come through here — it
    // takes the synchronous _doSwap hot-path in _onEnded.)
    advanceToPreloaded: async function () {
        const sw = this._doSwap();
        if (!sw) return false;
        try { await sw; } catch (e) { return false; }
        const el = this._activeEl();
        let stalled = false;
        const onWaiting = function () { stalled = true; };
        el.addEventListener('waiting', onWaiting);
        await new Promise(r => setTimeout(r, 250));  // let an immediate stall surface (spec §6)
        el.removeEventListener('waiting', onWaiting);
        return !stalled;
    },

    // Ready = buffered from the start, enough to play through the hop.
    _isReady: function (el) {
        if (!el || el.readyState < 3) return false;        // < HAVE_FUTURE_DATA
        try {
            const b = el.buffered;
            if (!b || b.length === 0) return false;
            if (b.start(0) > 0.05) return false;            // must cover the start
            const have = b.end(0);
            const dur = isFinite(el.duration) ? el.duration : have;
            return have >= Math.min(2, dur);                // ~2s, or whole short track
        } catch (e) { return false; }
    },

    toggle: function () {
        const el = this._activeEl();
        if (!el) return;
        if (el.paused) {
            const p = el.play();
            if (p && typeof p.catch === 'function') p.catch(() => { });
        } else {
            el.pause();
        }
    },
    stop: function () {
        if (!this._els) return;
        for (const el of this._els) {
            el.pause();
            el.removeAttribute('src');
            el._preloadFailed = false;
            try { el.load(); } catch (e) { }
        }
    },
    setCurrentTime: function (t) {
        const el = this._activeEl();
        if (el && isFinite(t)) { try { el.currentTime = Math.max(0, t); } catch (e) { } }
    },
    setVolume: function (v) {
        if (!this._els) return;
        const vol = Math.min(1, Math.max(0, v));
        for (const el of this._els) el.volume = vol;   // both, so volume survives a swap
    },
    seekProgressClick: function (clientX) {
        const el = this._activeEl();
        const bar = document.querySelector('.progress-bar-track');
        if (!el || !bar || !isFinite(el.duration) || el.duration <= 0) return;
        const r = bar.getBoundingClientRect();
        if (r.width <= 0) return;
        const frac = Math.min(1, Math.max(0, (clientX - r.left) / r.width));
        try { el.currentTime = frac * el.duration; } catch (e) { }
    },
    isPaused: function () {
        const el = this._activeEl();
        return el ? el.paused : true;
    },

    bindGlobalKeys: function (dotnetRef) {
        if (this._handler) {
            document.removeEventListener('keydown', this._handler);
        }
        this._handler = function (e) {
            const t = e.target;
            const tag = t ? t.tagName : '';
            const inField = t && (
                tag === 'INPUT' ||
                tag === 'TEXTAREA' ||
                t.isContentEditable === true
            );
            const activatable = t && (
                tag === 'BUTTON' || tag === 'SUMMARY' || tag === 'A' || tag === 'SELECT' ||
                (t.getAttribute && (t.getAttribute('role') === 'button' || t.getAttribute('role') === 'slider'))
            );
            if (e.key === '/' && !inField) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', '/');
            } else if (e.key === ' ' && !inField && !activatable) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', ' ');
            } else if ((e.key === 'n' || e.key === 'N') && !inField) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', 'n');
            } else if ((e.key === 'p' || e.key === 'P') && !inField) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', 'p');
            } else if ((e.key === 'd' || e.key === 'D') && !inField) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', 'd');
            } else if (e.key === 'Escape' && inField) {
                t.blur();
            }
        };
        document.addEventListener('keydown', this._handler);
    },

    // --- localStorage (dashboard panel state persistence) ----------------
    lsGet: function (key) {
        try { return localStorage.getItem(key); } catch (e) { return null; }
    },
    lsSet: function (key, value) {
        try { localStorage.setItem(key, value); } catch (e) { /* private mode */ }
    },

    // --- dashboard audio telemetry (binds to the ACTIVE element) ----------
    _bindStats: function (el) {
        if (!el || !this._dotnetRef) return;
        this._unbindStats();
        const self = this;
        this._statsEl = el;
        let lastFire = 0;

        function snapshot() {
            let bufferedAhead = 0;
            try {
                const b = el.buffered, ct = el.currentTime;
                for (let i = 0; i < b.length; i++) {
                    if (b.start(i) <= ct && ct <= b.end(i)) { bufferedAhead = b.end(i) - ct; break; }
                }
            } catch (e) { }
            return {
                currentTime: el.currentTime || 0,
                duration: isFinite(el.duration) ? el.duration : 0,
                bufferedAhead: bufferedAhead,
                networkState: el.networkState,
                readyState: el.readyState,
                paused: el.paused,
                volume: el.volume,
                playbackRate: el.playbackRate,
                decodedBytes: el.webkitAudioDecodedByteCount || 0,
                rebufferCount: self._rebufferCount
            };
        }
        function fire() {
            if (!self._dotnetRef) return;
            self._dotnetRef.invokeMethodAsync('OnAudioStats', snapshot()).catch(function () { });
        }
        function throttled() {
            const now = Date.now();
            if (now - lastFire < 200) return;
            lastFire = now;
            fire();
        }
        function rebuffer() { self._rebufferCount++; fire(); }

        this._statsListeners = {
            timeupdate: throttled,
            progress: throttled,
            waiting: rebuffer,
            stalled: rebuffer,
            playing: fire,
            pause: fire,
            volumechange: fire,
            ratechange: fire,
            loadedmetadata: fire
        };
        for (const evt in this._statsListeners) {
            el.addEventListener(evt, this._statsListeners[evt]);
        }
        fire();   // immediate snapshot so the transport/dashboard re-sync at once
    },
    _unbindStats: function () {
        if (this._statsEl && this._statsListeners) {
            for (const evt in this._statsListeners) {
                this._statsEl.removeEventListener(evt, this._statsListeners[evt]);
            }
        }
        this._statsEl = null;
        this._statsListeners = null;
    }
};
