#!/usr/bin/env python3
"""Synthesises AVATrackAndField's sound effects and tunes into ../src/AVATrackAndField/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, saw, sine and noise voices, and the crowd
from filtered noise. The title plays the Ode to Joy, the theme of the last movement of Beethoven's Ninth Symphony
(1824), arranged for brass over a march on the drums; the fanfares and jingles are new. The starter's calls are spoken
by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVATrackAndField", "Sounds")
RNG = random.Random(1983)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=6.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t) * min(1.0, t / 0.15)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def gain(samples, g):
    return [s * g for s in samples]


def bandpass(samples, freq, q=1.0):
    """A two-pole band-pass filter (the cookbook biquad): noise through it becomes a hiss, a roar, a rumble."""
    w0 = 2 * math.pi * freq / RATE
    alpha = math.sin(w0) / (2 * q)
    b0, b2 = alpha, -alpha
    a0, a1, a2 = 1 + alpha, -2 * math.cos(w0), 1 - alpha
    x1 = x2 = y1 = y2 = 0.0
    out = []
    for x in samples:
        y = (b0 * x + b2 * x2 - a1 * y1 - a2 * y2) / a0
        x2, x1, y2, y1 = x1, x, y1, y
        out.append(y)
    return out


def noise(seconds):
    return [RNG.uniform(-1, 1) for _ in range(int(RATE * seconds))]


def envelope(samples, points):
    """Shapes samples by straight lines through (time, level) points."""
    out = []
    for i, s in enumerate(samples):
        t = i / RATE
        level = points[-1][1]
        for (t0, l0), (t1, l1) in zip(points, points[1:]):
            if t0 <= t <= t1:
                level = l0 + (l1 - l0) * (t - t0) / max(1e-9, t1 - t0)
                break
        out.append(s * level)
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def brass(seconds, note, gain_=0.5):
    """A brass voice: a saw and a square together, a soft swell in, a little vibrato as the note holds."""
    f = hz(note)
    body = mix(tone(seconds, f, kind="saw", attack=0.03, release=0.35, vibrato=0.006, gain=gain_),
               tone(seconds, f * 1.003, kind="square", attack=0.04, release=0.4, vibrato=0.006, gain=gain_ * 0.35))
    return bandpass(body, min(3200, f * 3.5), 0.7)


def line(bpm, notes, instrument, **kw):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else instrument(d, note, **kw))
    return out


def bass(seconds, note, gain_=0.55):
    return tone(seconds, hz(note), kind="tri", attack=0.01, release=0.5, gain=gain_)


def fit(samples, seconds):
    """Cut or padded to just so long."""
    n = int(RATE * seconds)
    return (samples + [0.0] * max(0, n - len(samples)))[:n]


def timpani(seconds, gain_=1.0):
    hit = mix(tone(0.4, 98, 82, "sine", attack=0.002, release=1.2, gain=gain_), gain(bandpass(noise(0.06), 400, 1.0), gain_ * 0.6))
    return fit(hit, seconds)


def snare(seconds, gain_=0.5):
    hit = mix(gain(bandpass(noise(0.12), 2400, 0.8), gain_), tone(0.06, 220, 160, "tri", attack=0.001, release=1.0, gain=gain_ * 0.5))
    return fit(envelope(hit, [(0, 1), (0.12, 0)]), seconds)


def march(bpm, bars):
    """A march on the drums: the timpani on the beat, the snare off it, a roll to finish each four bars."""
    beat = 60.0 / bpm
    out = []
    for b in range(bars):
        if b % 4 == 3:
            out.extend(cat(timpani(beat), snare(beat / 2, 0.4), snare(beat / 2, 0.3), *[snare(beat / 4, 0.25 + 0.05 * i) for i in range(8)]))
        else:
            out.extend(cat(timpani(beat), snare(beat / 2, 0.4), snare(beat / 2, 0.25), timpani(beat, 0.7), snare(beat, 0.4)))
    return out


def ode_to_joy():
    """The Ode to Joy (Beethoven, 1824): the melody in D, as the whole orchestra first takes it up."""
    q, h, dq, e = 1, 2, 1.5, 0.5
    a = [("F#5", q), ("F#5", q), ("G5", q), ("A5", q), ("A5", q), ("G5", q), ("F#5", q), ("E5", q),
         ("D5", q), ("D5", q), ("E5", q), ("F#5", q)]
    first = a + [("F#5", dq), ("E5", e), ("E5", h)]
    second = a + [("E5", dq), ("D5", e), ("D5", h)]
    middle = [("E5", q), ("E5", q), ("F#5", q), ("D5", q), ("E5", q), ("F#5", e), ("G5", e), ("F#5", q), ("D5", q),
              ("E5", q), ("F#5", e), ("G5", e), ("F#5", q), ("E5", q), ("D5", q), ("E5", q), ("A4", h)]
    return first + second + middle + second


def ode_bass():
    """Under it, the roots: D and A, a bar or half a bar each."""
    h = 2
    a = [("D3", h), ("D3", h), ("A2", h), ("A2", h), ("D3", h), ("D3", h)]
    first = a + [("A2", h), ("A2", h)]
    second = a + [("A2", h), ("D3", h)]
    middle = [("A2", h), ("D3", h), ("A2", h), ("D3", h), ("A2", h), ("E3", h), ("D3", h), ("A2", h)]
    return first + second + middle + second


def crowd(seconds, swell=0.0):
    """A stadium crowd: a low roar of many voices (noise through a few vocal bands), rising and falling."""
    n = noise(seconds)
    roar = mix(gain(bandpass(n, 380, 0.9), 1.0), gain(bandpass(n, 900, 1.2), 0.7), gain(bandpass(n, 2200, 1.5), 0.35))
    out, level = [], 0.6
    target = 0.6
    for i, s in enumerate(roar):
        if i % 2205 == 0:
            target = 0.45 + 0.35 * RNG.random() + swell
        level += (target - level) * 0.0004
        out.append(s * level)
    # A few voices calling out above the roar.
    for _ in range(int(seconds * 2)):
        start = RNG.randrange(0, max(1, len(out) - RATE))
        f = RNG.uniform(220, 520)
        call = tone(RNG.uniform(0.2, 0.5), f, f * RNG.uniform(0.85, 1.2), "saw", attack=0.05, release=1.2, vibrato=0.03, vib_rate=5, gain=0.04 + swell * 0.05)
        call = bandpass(call, f * 2.5, 1.0)
        for j, v in enumerate(call):
            out[start + j] += v
    return out


def loop(samples, fade=0.6):
    """Joins the end to the start, so it loops without a seam."""
    k = int(RATE * fade)
    body = samples[:-k]
    for i in range(k):
        t = i / k
        body[i] = body[i] * t + samples[len(samples) - k + i] * (1 - t)
    return body


def cheer(seconds):
    """The crowd on its feet: the roar swelling up fast and dying away, whistles over it."""
    c = crowd(seconds, swell=0.8)
    c = envelope(c, [(0, 0.2), (0.25, 1.0), (seconds * 0.5, 0.9), (seconds, 0.0)])
    for _ in range(6):
        start = RNG.randrange(int(RATE * 0.1), int(RATE * seconds * 0.5))
        f = RNG.uniform(1800, 2600)
        w = tone(0.35, f, f * RNG.choice([1.25, 0.8]), "sine", attack=0.02, release=0.8, gain=0.12)
        for j, v in enumerate(w):
            if start + j < len(c):
                c[start + j] += v
    return c


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The starter's pistol: a crack and its echo off the stand.
    crack = mix(gain(bandpass(noise(0.25), 1800, 0.6), 1.0), tone(0.12, 160, 60, "sine", attack=0.001, release=1.5, gain=0.8))
    crack = envelope(crack, [(0, 1), (0.02, 0.6), (0.25, 0)])
    echo = gain(envelope(bandpass(noise(0.4), 900, 0.8), [(0, 0), (0.05, 0.3), (0.4, 0)]), 0.6)
    write("Gun.wav", cat(crack, echo), 0.8)
    # A spike on the tartan.
    write("Step.wav", envelope(mix(bandpass(noise(0.06), 700, 1.2), tone(0.05, 140, 80, "sine", attack=0.001, release=1.0, gain=0.6)), [(0, 1), (0.06, 0)]), 0.25)
    write("Jump.wav", mix(tone(0.22, 260, 620, "tri", attack=0.005, release=0.8, gain=0.4), envelope(bandpass(noise(0.2), 1400, 1.0), [(0, 0.2), (0.08, 1), (0.2, 0)])), 0.4)
    # The javelin or the hammer away: a rush of air.
    rush = envelope(noise(0.5), [(0, 0), (0.1, 1), (0.5, 0)])
    write("Throw.wav", mix(bandpass(rush, 1100, 1.2), tone(0.4, 520, 300, "saw", attack=0.02, release=1.0, gain=0.05)), 0.5)
    write("Swish.wav", bandpass(envelope(noise(0.25), [(0, 0), (0.12, 1), (0.25, 0)]), 1600, 1.5), 0.3)
    write("Land.wav", mix(envelope(bandpass(noise(0.3), 300, 0.8), [(0, 1), (0.3, 0)]), tone(0.2, 90, 45, "sine", attack=0.002, release=1.2, gain=0.8)), 0.6)
    # A hurdle going over, or the bar coming down: a clatter of metal.
    clank = mix(*[tone(0.3, f, f * 0.98, "square", attack=0.001, release=2.0, gain=0.12) for f in (523, 790, 1210, 1667)])
    write("Knock.wav", mix(clank, envelope(bandpass(noise(0.15), 3000, 1.0), [(0, 1), (0.15, 0)]),
                           cat(silence(0.12), gain(clank[:int(RATE * 0.2)], 0.5))), 0.45)
    # The foul: the officials' buzzer.
    write("Foul.wav", mix(tone(0.7, 110, kind="square", attack=0.005, release=0.15, gain=0.5), tone(0.7, 117, kind="square", attack=0.005, release=0.15, gain=0.5)), 0.4)
    write("Cheer.wav", cheer(2.6), 0.6)
    write("Crowd.wav", loop(crowd(8.6)), 0.45)
    # The starter's calls, and the foul called.
    write("SayMarks.wav", say("AA1 N , Y AO R , M AA1 R K S !", rate=RATE, pitch=105, speed=0.85, size=1.1), 0.6)
    write("SaySet.wav", say("S EH1 T !", rate=RATE, pitch=115, speed=0.8, size=1.1), 0.6)
    write("SayFoul.wav", say("F AA1 UW L !", rate=RATE, pitch=100, speed=0.85, size=1.1), 0.6)

    # The event's fanfare, before each event: trumpets up the chord.
    write("Fanfare.wav", line(150, [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 1.5), ("A5", 0.5), ("D6", 2)], brass), 0.42)
    # Qualified: a bright call.
    write("Qualify.wav", mix(line(170, [("A5", 0.5), ("A5", 0.5), ("A5", 0.5), ("D6", 2.5)], brass),
                             line(170, [("F#5", 0.5), ("F#5", 0.5), ("F#5", 0.5), ("A5", 2.5)], brass, gain_=0.3)), 0.45)
    # A world record: the full flourish, over the timpani.
    rec = [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 1), ("C#6", 0.5), ("D6", 0.5), ("E6", 0.5), ("F#6", 3)]
    write("Record.wav", mix(line(160, rec, brass),
                            line(160, [("D3", 2), ("A2", 1), ("D3", 1), ("A2", 1), ("D3", 3)], bass), cat(timpani(0.375), timpani(0.375), timpani(0.375), timpani(2.4))), 0.45)
    # Failed to qualify: falling away.
    write("Fail.wav", line(110, [("A4", 0.75), ("G#4", 0.75), ("G4", 0.75), ("F#4", 2.5)], brass), 0.4)
    write("GameOver.wav", mix(line(80, [("D5", 1), ("C5", 1), ("Bb4", 1), ("A4", 1), ("F4", 1), ("D4", 3)], brass),
                              line(80, [("D3", 2), ("G2", 2), ("D3", 4)], bass)), 0.42)
    # The title: the Ode to Joy for brass, over a march.
    bpm = 116
    melody = line(bpm, ode_to_joy(), brass)
    low = line(bpm, ode_bass(), bass)
    drums = march(bpm, 16)
    write("TitleTune.wav", cat(mix(melody, low, gain(drums, 0.7)), silence(1.5)), 0.42)


if __name__ == "__main__":
    main()
