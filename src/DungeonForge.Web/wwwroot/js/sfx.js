// Sound and haptics for the stage, synthesised with WebAudio so there are no assets to ship or cache.
// Cues come from StageDirector: tick, hit, miss, crit, fumble, win, lose. Anything unknown is ignored.
(function () {
    var MUTE_KEY = "dungeonforge.muted";
    var ctx = null;
    var muted = false;

    try { muted = localStorage.getItem(MUTE_KEY) === "1"; } catch (e) { }

    function audio() {
        if (!ctx) {
            var Ctor = window.AudioContext || window.webkitAudioContext;
            if (!Ctor) return null;
            ctx = new Ctor();
        }
        if (ctx.state === "suspended") ctx.resume();
        return ctx;
    }

    // One enveloped oscillator note; freqEnd slides the pitch for whooshes and drops.
    function tone(c, type, freq, start, dur, gain, freqEnd) {
        var osc = c.createOscillator();
        var amp = c.createGain();
        var t = c.currentTime + start;
        osc.type = type;
        osc.frequency.setValueAtTime(freq, t);
        if (freqEnd) osc.frequency.exponentialRampToValueAtTime(freqEnd, t + dur);
        amp.gain.setValueAtTime(0.0001, t);
        amp.gain.exponentialRampToValueAtTime(gain, t + 0.01);
        amp.gain.exponentialRampToValueAtTime(0.0001, t + dur);
        osc.connect(amp).connect(c.destination);
        osc.start(t);
        osc.stop(t + dur + 0.02);
    }

    function noise(c, start, dur, gain) {
        var len = Math.floor(c.sampleRate * dur);
        var buf = c.createBuffer(1, len, c.sampleRate);
        var data = buf.getChannelData(0);
        for (var i = 0; i < len; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / len);
        var src = c.createBufferSource();
        var amp = c.createGain();
        src.buffer = buf;
        amp.gain.value = gain;
        src.connect(amp).connect(c.destination);
        src.start(c.currentTime + start);
    }

    var cues = {
        tick: function (c) { tone(c, "square", 900, 0, 0.04, 0.05); },
        hit: function (c) { noise(c, 0, 0.12, 0.18); tone(c, "sine", 180, 0, 0.18, 0.3, 60); },
        miss: function (c) { tone(c, "sine", 420, 0, 0.22, 0.12, 140); },
        crit: function (c) {
            noise(c, 0, 0.14, 0.2);
            tone(c, "sine", 180, 0, 0.18, 0.3, 60);
            [523, 659, 784, 1047].forEach(function (f, i) { tone(c, "triangle", f, 0.08 + i * 0.07, 0.2, 0.16); });
        },
        fumble: function (c) { tone(c, "sawtooth", 220, 0, 0.5, 0.14, 55); tone(c, "square", 110, 0.1, 0.4, 0.08, 40); },
        win: function (c) { [392, 494, 587, 784].forEach(function (f, i) { tone(c, "triangle", f, i * 0.1, 0.35, 0.18); }); },
        lose: function (c) { [392, 349, 311, 262].forEach(function (f, i) { tone(c, "sawtooth", f, i * 0.14, 0.4, 0.09); }); }
    };

    var buzz = { hit: 30, crit: [30, 40, 60], fumble: [80, 40, 80], lose: 200 };

    window.dungeonForge = window.dungeonForge || {};
    window.dungeonForge.sfx = {
        play: function (name) {
            if (muted || !cues[name]) return;
            try {
                var c = audio();
                if (c) cues[name](c);
                if (buzz[name] && navigator.vibrate) navigator.vibrate(buzz[name]);
            } catch (e) { }
        },
        isMuted: function () { return muted; },
        // Returns the new muted state; the choice is remembered across visits.
        toggle: function () {
            muted = !muted;
            try { localStorage.setItem(MUTE_KEY, muted ? "1" : "0"); } catch (e) { }
            if (!muted) window.dungeonForge.sfx.play("tick");
            return muted;
        }
    };
})();
