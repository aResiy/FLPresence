# name=FLPresence
# url=https://github.com/pokazakus/FLPresence
# -*- coding: utf-8 -*-
"""FLPresence Bridge - FL Studio MIDI controller script.

Collects FL Studio session state through the official MIDI Scripting API and
sends it as small JSON datagrams over UDP (localhost only) to the
FLPresence Companion app. Fire-and-forget: if the Companion is not running
the datagrams are dropped by the OS and this script never errors.

Liveness model (v1.1):
  * A daemon thread sends "ping" heartbeats every 2 s. The thread touches
    NOTHING but a socket and two floats - no FL API calls from a foreign
    thread - so it can never crash or stall FL Studio.
  * Any callback (OnIdle, OnRefresh, notes, ...) refreshes the actual state.
    Full state is also re-sent as a heartbeat every 2 s.
  * FL state therefore does NOT depend on a MIDI controller being connected
    or sending data. The script instance just has to be assigned to some
    always-present MIDI input (a virtual loopMIDI port is the recommended
    permanent home); the physical keyboard is only an optional note source.

Everything here is defensive: callbacks are wrapped, no call may block,
and any internal error is reported to the Companion over UDP instead of
dying silently. Only verified API functions are used; undocumented extras
are probed with hasattr().

Compatible with Python 3.7+ (FL Studio's bundled interpreter).
"""

import json
import os
import socket
import sys
import threading
import time
import traceback

try:
    import channels
    import device
    import general
    import midi
    import mixer
    import patterns
    import plugins
    import transport
    import ui
except ImportError:
    # Running outside FL Studio (self-test / CI). Stub every API module so
    # attribute access succeeds but any call raises AttributeError, which
    # _safe() converts to None. build_state() then yields a harmless skeleton.
    import types

    class _ApiStub(types.ModuleType):
        def __getattr__(self, name):
            def _missing(*args, **kwargs):
                raise AttributeError(
                    "FL Studio API unavailable outside FL Studio: " + name)
            return _missing

    for _mod in ("channels", "device", "general", "midi", "mixer",
                 "patterns", "plugins", "transport", "ui"):
        sys.modules[_mod] = _ApiStub(_mod)

    import channels
    import device
    import general
    import midi
    import mixer
    import patterns
    import plugins
    import transport
    import ui

# --- config -----------------------------------------------------------------
HOST = "127.0.0.1"
PORT = 39901            # must match Companion's IPC listener port
MIN_SEND_GAP = 0.25     # s, min gap between full-state builds (debounce)
NOTES_SEND_GAP = 0.15   # s, min gap between note-set sends (throttle)
HEARTBEAT_GAP = 2.0     # s, periodic full state = liveness signal
PING_GAP = 2.0          # s, keepalive-thread ping period
MAX_NOTES = 8           # cap of tracked held notes
TRACE = True            # local trace file (see _trace); off in production? no.

# --- module state ------------------------------------------------------------
_sock = None
_source = "unknown"     # device.getName() - which port this instance serves
_last_full = 0.0        # monotonic time of last full-state send
_last_hb = 0.0
_last_notes = 0.0
_session_start = 0.0    # wall clock, session timer for Discord timestamps
_held = []              # currently held MIDI notes (ordered, deduplicated)
_vel = {}               # note -> last seen velocity
_last_state_sig = None  # signature of last sent full state
_last_notes_sig = None
_error_reported = {}    # callback name -> monotonic time of last error report


def _utcnow():
    return time.time()


def _send(payload):
    """Fire-and-forget UDP send. Never raises, never blocks meaningfully."""
    global _sock
    try:
        if _sock is None:
            _sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        _sock.sendto(json.dumps(payload).encode("ascii"), (HOST, PORT))
    except Exception:
        # Companion not running / socket gone: stay silent, keep working.
        pass


def _trace_path():
    base = os.environ.get("APPDATA") or os.environ.get("TEMP") or "."
    name = "bridge_trace.log"
    try:
        dev = device.getName()
        if dev:
            safe = "".join(c if c.isalnum() else "_" for c in str(dev))
            name = "bridge_trace_%s.log" % safe
    except Exception:
        pass
    return os.path.join(base, "FLPresence", name)


_trace_lock = threading.Lock()
_trace_last = {}        # key -> monotonic time of last write (rate limit)


def _trace(msg, key=None):
    """Append a line to the trace file. Rate-limited to 1 line/s per key so
    high-frequency callbacks cannot grow the file without bound; the file is
    also hard-capped at 256 KB."""
    if not TRACE:
        return
    try:
        now = time.monotonic()
        key = key or msg
        with _trace_lock:
            if now - _trace_last.get(key, 0) < 1.0:
                return
            _trace_last[key] = now
            path = _trace_path()
            os.makedirs(os.path.dirname(path), exist_ok=True)
            try:
                if os.path.getsize(path) > 256 * 1024:
                    os.remove(path)
            except OSError:
                pass
            with open(path, "a", encoding="utf-8", errors="replace") as fh:
                fh.write("%.3f %s\n" % (time.time(), msg))
    except Exception:
        pass


def _report_error(where, exc):
    """Report an internal error to the Companion (UDP) + trace, max once
    per 5 s per callback so a broken API call cannot spam anything."""
    try:
        now = time.monotonic()
        if now - _error_reported.get(where, 0) < 5.0:
            return
        _error_reported[where] = now
        tb = traceback.format_exception_only(type(exc), exc)
        msg = ("".join(tb)).strip()[-300:]
        _trace("ERROR in %s: %s" % (where, msg), key="err:" + where)
        _send({"type": "error", "where": where, "message": msg,
               "t": _utcnow()})
    except Exception:
        pass


def _safe(fn, *args, **kw):
    """Call an FL API function, return None on any failure."""
    try:
        return fn(*args, **kw)
    except Exception:
        return None


def _name(s):
    """Normalize an API-returned name to a clean string."""
    if s is None:
        return ""
    try:
        s = str(s).strip()
    except Exception:
        return ""
    return s


def _project_name():
    """Best-effort project name.

    Documented API only exposes general.getProjectTitle() (F11 title field).
    Some builds also expose getProjectFilename(); probe it defensively and
    prefer the filename when the title is empty.
    """
    title = _name(_safe(general.getProjectTitle))
    if title:
        return title
    fn = None
    if hasattr(general, "getProjectFilename"):
        fn = _name(_safe(general.getProjectFilename))
    if fn:
        # keep the file name part only
        fn = fn.replace("\\", "/").split("/")[-1]
        return fn
    return ""


def _fl_version():
    # ui.getVersion(5) = full version + edition, e.g. "24.2.2 Producer"
    v = _safe(ui.getVersion, 5)
    if v:
        return _name(v)
    v = _safe(ui.getVersion, 4)
    return _name(v) if v else ""


def build_state():
    """Snapshot of everything the official API exposes. All fields may be
    empty/None when the API refuses - Companion handles both."""
    playing = bool(_safe(transport.isPlaying) or 0)
    recording = bool(_safe(transport.isRecording) or 0)
    song_mode = _safe(transport.getLoopMode)
    pattern_no = _safe(patterns.patternNumber)

    ch = _safe(channels.selectedChannel, 1)  # 1 => -1 when nothing selected
    ch_name = ""
    plugin = ""
    if ch is not None and ch >= 0:
        ch_name = _name(_safe(channels.getChannelName, ch))
        plugin = _name(_safe(plugins.getPluginName, ch))

    mix_tr = _safe(mixer.trackNumber)
    mix_name = ""
    if mix_tr is not None and mix_tr >= 0:
        mix_name = _name(_safe(mixer.getTrackName, mix_tr))

    bpm = _safe(mixer.getCurrentTempo, 1)  # asInt=True
    if bpm is None:
        bpm = _safe(mixer.getCurrentTempo)

    pos_ms = _safe(transport.getSongPos, midi.SONGLENGTH_MS)
    len_ms = _safe(transport.getSongLength, midi.SONGLENGTH_MS)

    metronome = bool(_safe(ui.isMetronomeEnabled) or 0)

    ppq = _safe(general.getRecPPQ)
    ppb = _safe(general.getRecPPB)
    ts_num = None
    try:
        if ppq and ppb:
            ts_num = int(round(ppb / float(ppq)))
    except Exception:
        ts_num = None

    return {
        "type": "state",
        "t": _utcnow(),
        "session": round(_utcnow() - _session_start),
        "source": _source,
        "flVersion": _fl_version(),
        "project": _project_name(),
        "playing": playing,
        "recording": recording,
        "songMode": (song_mode == 1) if song_mode is not None else None,
        "patternNumber": pattern_no,
        "pattern": _name(_safe(patterns.getPatternName, pattern_no))
        if pattern_no is not None else "",
        "channelIndex": ch,
        "channel": ch_name,
        "plugin": plugin,
        "mixerTrack": mix_tr,
        "mixerTrackName": mix_name,
        "bpm": bpm,
        "posMs": pos_ms,
        "lenMs": len_ms,
        "metronome": metronome,
        "tsNum": ts_num,
        "notes": list(_held[:MAX_NOTES]),
        "velocities": {str(n): _vel.get(n, 0) for n in _held[:MAX_NOTES]},
        "notesCount": len(_held),
    }


def _signature(st):
    """State signature WITHOUT volatile fields (t/session) - used to detect
    real changes so a change is never swallowed by the heartbeat timer."""
    try:
        copy = dict(st)
        copy.pop("t", None)
        copy.pop("session", None)
        return json.dumps(copy, sort_keys=True)
    except Exception:
        return None


def _maybe_send_full(now):
    """Send full state on any visible change (debounced) or as a periodic
    heartbeat. The state is REBUILT here (cheap C getters) instead of
    trusting _dirty flags, so nothing can be missed."""
    global _last_full, _last_hb, _last_state_sig
    if (now - _last_full) < MIN_SEND_GAP:
        return
    st = build_state()
    sig = _signature(st)
    due_hb = (now - _last_hb) >= HEARTBEAT_GAP
    if sig != _last_state_sig or due_hb:
        _send(st)
        _last_state_sig = sig
        _last_full = now
        _last_hb = now
        _trace("full state sent (change=%s hb=%s)"
               % (sig != _last_state_sig, due_hb), key="full")


def _maybe_send_notes(now):
    """Push the held-note set (with velocities) on change, throttled."""
    global _last_notes, _last_notes_sig
    if (now - _last_notes) < NOTES_SEND_GAP:
        return
    sig = json.dumps([_held[:MAX_NOTES],
                      {str(n): _vel.get(n, 0) for n in _held[:MAX_NOTES]}],
                     sort_keys=True)
    if sig == _last_notes_sig:
        return
    _send({
        "type": "notes",
        "t": _utcnow(),
        "notes": list(_held[:MAX_NOTES]),
        "velocities": {str(n): _vel.get(n, 0) for n in _held[:MAX_NOTES]},
        "notesCount": len(_held),
    })
    _last_notes = now
    _last_notes_sig = sig


def _tick():
    """Common body for every callback: refresh full state + pending notes."""
    now = _utcnow()
    _maybe_send_full(now)
    if _held:
        _maybe_send_notes(now)


# --- keepalive thread ---------------------------------------------------------

_thread = None
_thread_stop = None


def _keepalive(stop):
    """Ping-only liveness thread. Deliberately touches NO FL API: a bare
    socket send cannot crash or block FL Studio regardless of its state."""
    while not stop.wait(PING_GAP):
        _send({"type": "ping", "t": _utcnow(),
               "session": round(_utcnow() - _session_start),
               "source": _source})


# --- FL Studio callbacks ------------------------------------------------------

def OnInit():
    global _session_start, _source, _thread, _thread_stop
    _session_start = _utcnow()
    _sock = None
    _last_state_sig = None
    _last_notes_sig = None
    _source = _name(_safe(device.getName)) or "unknown"
    _trace("OnInit (source=%s)" % _source, key="init")
    _send({"type": "hello", "t": _utcnow(), "script": "FLPresence",
           "source": _source})
    # (Re)start the keepalive thread; survives device re-connects.
    try:
        if _thread_stop is not None:
            _thread_stop.set()
        if _thread is None or not _thread.is_alive():
            _thread_stop = threading.Event()
            _thread = threading.Thread(target=_keepalive,
                                       args=(_thread_stop,), daemon=True)
            _thread.start()
    except Exception as exc:
        _report_error("OnInit(thread)", exc)


def OnDeInit():
    # May not fire on process kill; Companion's heartbeat timeout covers that.
    global _thread_stop
    try:
        _send({"type": "bye", "t": _utcnow(), "source": _source})
    except Exception:
        pass
    try:
        if _thread_stop is not None:
            _thread_stop.set()
    except Exception:
        pass
    _trace("OnDeInit", key="deinit")


def OnFirstConnect():
    _trace("OnFirstConnect", key="firstconnect")
    _tick()


def OnIdle():
    _trace("OnIdle", key="idle")
    _tick()


def OnRefresh(flags):
    _trace("OnRefresh flags=%s" % flags, key="refresh")
    _tick()


def OnDoFullRefresh():
    _trace("OnDoFullRefresh", key="fullrefresh")
    _tick()


def OnProjectLoad():
    _trace("OnProjectLoad", key="projload")
    _tick()


def OnDirtyChannel(channelIndex):
    _trace("OnDirtyChannel %s" % channelIndex, key="dirtych")
    _tick()


def OnDirtyMixerTrack(trackIndex):
    _trace("OnDirtyMixerTrack %s" % trackIndex, key="dirtymix")
    _tick()


def OnUpdateBeatIndicator(value):
    # While playing, drive position/state updates at beat rate (not spammy).
    _trace("OnUpdateBeatIndicator %s" % value, key="beat")
    _tick()


def OnNoteOn(event):
    try:
        note = event.data1
        vel = event.data2
    except Exception as exc:
        _report_error("OnNoteOn", exc)
        return
    if note is None:
        return
    try:
        if note not in _held:
            if len(_held) < MAX_NOTES:
                _held.append(note)
        _vel[note] = vel or 0
    except Exception as exc:
        _report_error("OnNoteOn", exc)
        return
    _trace("OnNoteOn note=%s vel=%s" % (note, vel), key="noteon")
    _tick()


def OnNoteOff(event):
    try:
        note = event.data1
    except Exception as exc:
        _report_error("OnNoteOff", exc)
        return
    try:
        if note in _held:
            _held.remove(note)
        _vel.pop(note, None)
    except Exception as exc:
        _report_error("OnNoteOff", exc)
        return
    _trace("OnNoteOff note=%s" % note, key="noteoff")
    _tick()


# Local self-test (not used by FL Studio): `python device_FLPresence.py`
if __name__ == "__main__":
    _session_start = _utcnow()
    st = build_state()
    assert isinstance(st, dict) and st["type"] == "state"
    assert "bpm" in st and "notes" in st and "source" in st
    sig1 = _signature(st)
    st["t"] += 5.0
    st["session"] += 5
    assert _signature(st) == sig1, "volatile fields must not change signature"
    st["bpm"] = (st["bpm"] or 0) + 1
    assert _signature(st) != sig1, "bpm change must change signature"
    # keepalive loop must exit when the stop event is set
    stop = threading.Event()
    stop.set()
    _keepalive(stop)
    print(json.dumps(st, indent=2, ensure_ascii=True))
    print("self-test OK (API calls fail gracefully outside FL Studio)")
    sys.exit(0)
