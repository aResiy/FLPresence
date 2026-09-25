"""Fake FLPresence bridge: sends scripted datagrams to a running Companion.

Usage: python tools/fake_bridge.py [scenario]
Scenarios: edit (default), play, record, chord, bye, timeout
Used for end-to-end testing without FL Studio.
"""
import json
import socket
import sys
import time

HOST, PORT = "127.0.0.1", 39901

def send(sock, payload):
    sock.sendto(json.dumps(payload).encode("ascii"), (HOST, PORT))
    print("sent:", payload.get("type"), {k: v for k, v in payload.items() if k in ("project", "playing", "recording", "pattern", "channel", "plugin", "bpm", "notes")})

def state(**kw):
    p = {"type": "state", "t": time.time(), "session": 300, "flVersion": "24.2.2 Producer",
         "project": "Test Melody.flp", "playing": False, "recording": False, "songMode": False,
         "patternNumber": 1, "pattern": "Verse", "channelIndex": 0, "channel": "Serum",
         "plugin": "Serum", "mixerTrack": 1, "mixerTrackName": "Serum", "bpm": 140,
         "posMs": 0, "lenMs": 64000, "metronome": False, "tsNum": 4,
         "notes": [], "velocities": {}, "notesCount": 0}
    p.update(kw)
    return p

def main():
    scenario = sys.argv[1] if len(sys.argv) > 1 else "edit"
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    send(s, {"type": "hello", "t": time.time()})
    time.sleep(0.4)

    if scenario == "edit":
        send(s, state())
        time.sleep(1.2)
        send(s, {"type": "notes", "t": time.time(), "notes": [61, 64, 68, 71],
                 "velocities": {"61": 100}, "notesCount": 4})
        time.sleep(1.2)
        send(s, {"type": "notes", "t": time.time(), "notes": [], "velocities": {}, "notesCount": 0})
        time.sleep(1.2)
    elif scenario == "play":
        send(s, state(playing=True, songMode=True, pattern="Chorus"))
        time.sleep(1.2)
    elif scenario == "record":
        send(s, state(recording=True, playing=True))
        time.sleep(1.2)
    elif scenario == "chord":
        send(s, state())
        send(s, {"type": "notes", "t": time.time(), "notes": [60, 64, 67, 71],
                 "velocities": {"60": 90}, "notesCount": 4})
        time.sleep(1.2)
    elif scenario == "bye":
        send(s, {"type": "bye", "t": time.time()})
    elif scenario == "timeout":
        send(s, state())
        print("waiting for companion heartbeat timeout (15s+)...")
        time.sleep(17)

    print("done:", scenario)

if __name__ == "__main__":
    main()
