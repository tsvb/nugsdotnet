// JS interop for the <audio> element + window-level keyboard shortcuts.
// Blazor's @onkeydown only fires when the bound element has focus, which
// makes global shortcuts awkward — we attach a window listener here instead
// and call back into Blazor via DotNetObjectReference.
window.audioInterop = {
    setSrcAndPlay: function (el, url) {
        if (!el) return;
        this._rebufferCount = 0;   // new track — reset the dashboard rebuffer counter
        el.src = url;
        const p = el.play();
        if (p && typeof p.catch === 'function') {
            p.catch(err => console.warn('play() rejected:', err));
        }
    },
    // Play/pause, seek and volume all locate the element themselves so any
    // component (transport, keyboard handler) can drive them with no ref.
    toggle: function () {
        const el = document.querySelector('audio.audio');
        if (!el) return;
        if (el.paused) {
            const p = el.play();
            if (p && typeof p.catch === 'function') p.catch(() => { });
        } else {
            el.pause();
        }
    },
    stop: function () {
        const el = document.querySelector('audio.audio');
        if (!el) return;
        el.pause();
        el.removeAttribute('src');
        try { el.load(); } catch (e) { }
    },
    setCurrentTime: function (t) {
        const el = document.querySelector('audio.audio');
        if (el && isFinite(t)) { try { el.currentTime = Math.max(0, t); } catch (e) { } }
    },
    setVolume: function (v) {
        const el = document.querySelector('audio.audio');
        if (el) el.volume = Math.min(1, Math.max(0, v));
    },
    // Seek from a click on the dashboard's now-playing meter (.progress-bar-track),
    // mapping the click x-position to a fraction of the track.
    seekProgressClick: function (clientX) {
        const el = document.querySelector('audio.audio');
        const bar = document.querySelector('.progress-bar-track');
        if (!el || !bar || !isFinite(el.duration) || el.duration <= 0) return;
        const r = bar.getBoundingClientRect();
        if (r.width <= 0) return;
        const frac = Math.min(1, Math.max(0, (clientX - r.left) / r.width));
        try { el.currentTime = frac * el.duration; } catch (e) { }
    },
    isPaused: function (el) {
        const a = el || document.querySelector('audio.audio');
        return a ? a.paused : true;
    },
    bindGlobalKeys: function (dotnetRef) {
        // Idempotent: removing any prior listener if hot-reloaded.
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
            // Elements where Space/Enter natively activate or scroll — never
            // hijack Space from them (WCAG 2.1.1 / 2.1.4).
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

    // --- dashboard audio telemetry ---------------------------------------
    // Pushes a stats snapshot to .NET on throttled timeupdate/progress and on
    // buffering events. Finds the <audio> element itself so the caller needs no
    // ElementReference. Idempotent; returns false if the element isn't present.
    bindStats: function (dotnetRef) {
        const el = document.querySelector('audio.audio');
        if (!el) return false;
        if (this._statsBound) return true;

        const self = this;
        this._statsRef = dotnetRef;
        this._statsEl = el;
        if (typeof this._rebufferCount !== 'number') this._rebufferCount = 0;
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
            if (!self._statsRef) return;
            self._statsRef.invokeMethodAsync('OnAudioStats', snapshot()).catch(function () { });
        }
        function throttled() {
            const now = Date.now();
            if (now - lastFire < 200) return;
            lastFire = now;
            fire();
        }
        function rebuffer() { self._rebufferCount++; fire(); }
        function onError() {
            // 415 (HLS) / 404 (no stream) / network/decode failure: tell .NET so
            // the UI can surface it and skip, instead of silently stalling.
            if (self._statsRef) self._statsRef.invokeMethodAsync('OnAudioError').catch(function () { });
        }

        this._statsListeners = {
            timeupdate: throttled,
            progress: throttled,
            waiting: rebuffer,
            stalled: rebuffer,
            playing: fire,
            pause: fire,
            volumechange: fire,
            ratechange: fire,
            loadedmetadata: fire,
            error: onError
        };
        for (const evt in this._statsListeners) {
            el.addEventListener(evt, this._statsListeners[evt]);
        }
        this._statsBound = true;
        fire();   // initial snapshot
        return true;
    },
    unbindStats: function () {
        if (!this._statsBound || !this._statsEl) return;
        for (const evt in this._statsListeners) {
            this._statsEl.removeEventListener(evt, this._statsListeners[evt]);
        }
        this._statsRef = null;
        this._statsEl = null;
        this._statsListeners = null;
        this._statsBound = false;
    }
};
