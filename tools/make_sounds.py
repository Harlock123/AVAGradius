#!/usr/bin/env python3
"""Synthesises AVAGradius's sound effects and tunes into ../src/AVAGradius/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions. The ship's computer speaks through the formant voice in speech.py, the way the
talking arcade games of the day spoke, so no recording or speech engine is needed. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAGradius", "Sounds")
RNG = random.Random(1984)


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


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
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


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def echoed(dry, taps=((0.03, 0.15),)):
    """A little ring of the loudspeaker."""
    out = dry + [0.0] * int(RATE * 0.3)
    for delay, gain in taps:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * gain
    return out


def computer(words):
    """The Halcyon's computer: a clear, level voice with a fifth humming under it, a little metallic."""
    voice = say(words, rate=RATE, pitch=142, speed=1.05, size=0.9)
    hum = say(words, rate=RATE, pitch=213, speed=1.05, size=0.9, seed=11)
    return echoed([a + 0.15 * b for a, b in zip(voice, hum)])


# The computer's lines, written out as phonemes.
LINES = {
    "SayGo": "HH AE1 L S IY AH N , L AO1 N JH !",
    "SayCore": "HH IH1 T , TH AH K AO1 R !",
    "SaySpeed": "S P IY1 D , AH1 P !",
    "SayMissile": "M IH1 S AH L !",
    "SayTwin": "T W IH1 N !",
    "SayLaser": "L EY1 Z ER !",
    "SayDrone": "D R OW1 N !",
    "SayGuard": "G AA1 R D !",
    "SayClear": "W EH1 L , D AH1 N !",
    "SayVictory": "TH AH M AA1 IY N D , IH Z , D AH1 N !",
}


def bounce(root, beats, octave_up=True):
    """An eighth-note bass bouncing between a note and its octave."""
    hi = root[:-1] + str(int(root[-1]) + 1)
    return [(root, 0.5), (hi, 0.5)] * int(beats)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The Halcyon's guns.
    write("Fire.wav", tone(0.07, 2200, 900, "square", attack=0.001, release=1.4, gain=0.5), 0.14)
    write("Laser.wav", tone(0.14, 2600, 1400, "saw", attack=0.001, vibrato=0.05, vib_rate=60, release=0.8, gain=0.5), 0.14)
    write("MissileLaunch.wav", tone(0.18, 900, 300, "noise", attack=0.004, release=1.2, gain=0.6), 0.16)
    # Hits and ends.
    write("Boom.wav", mix(tone(0.3, 2000, 200, "noise", attack=0.001, release=0.9), tone(0.25, 300, 70, "square", release=0.8, gain=0.5)), 0.3)
    write("BigBoom.wav", mix(tone(0.7, 2400, 90, "noise", attack=0.001, release=0.6), tone(0.6, 160, 35, "saw", vibrato=0.2, vib_rate=12, release=0.6, gain=0.6)), 0.42)
    write("Hit.wav", tone(0.05, 1300, 900, "square", attack=0.001, release=1.2, gain=0.5), 0.18)
    write("Clink.wav", tone(0.06, 3200, 2600, "tri", attack=0.001, release=1.5, gain=0.5), 0.14)
    write("Crack.wav", mix(tone(0.12, 4000, 1600, "noise", attack=0.001, release=1.4, gain=0.5), tone(0.08, 2400, 1800, "tri", release=1.2, gain=0.3)), 0.2)
    write("Die.wav", mix(tone(1.4, 2600, 60, "noise", attack=0.001, release=0.4), tone(1.2, 220, 30, "saw", vibrato=0.25, vib_rate=10, release=0.5, gain=0.6)), 0.5)
    # Capsules and power.
    write("Capsule.wav", cat(*[tone(0.05, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6"]]), 0.26)
    write("PowerUp.wav", cat(*[tone(0.04, hz(n), kind="square", release=0.3) for n in ["C5", "G5", "C6", "G6", "C7"]]), 0.24)
    write("Denied.wav", tone(0.16, 180, 160, "square", release=0.5, gain=0.5), 0.2)
    write("GuardHit.wav", tone(0.18, 600, 1800, "sine", attack=0.002, vibrato=0.1, vib_rate=40, release=0.8), 0.24)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["A5", "C#6", "E6", "A6", "E6", "A6"]]), 0.3)
    # The Varkh.
    write("EnemyFire.wav", tone(0.1, 700, 1100, "square", attack=0.001, release=1.0, gain=0.4), 0.12)
    write("Beam.wav", tone(0.45, 1800, 400, "saw", attack=0.01, vibrato=0.08, vib_rate=50, release=0.6), 0.26)
    write("Erupt.wav", mix(tone(0.4, 120, 60, "sine", attack=0.01, release=0.8), tone(0.35, 900, 200, "noise", attack=0.01, release=0.9, gain=0.5)), 0.26)
    write("RingOut.wav", tone(0.3, 500, 1500, "tri", attack=0.01, vibrato=0.12, vib_rate=18, release=0.6), 0.2)
    write("Spit.wav", tone(0.16, 320, 900, "square", attack=0.004, vibrato=0.06, vib_rate=30, release=0.8, gain=0.5), 0.18)
    write("BossCome.wav", cat(*[tone(0.16, hz(n), kind="saw", vibrato=0.02, release=0.4) for n in ["D3", "D3", "F3", "D3", "G#3"]], tone(0.6, hz("A3"), kind="saw", vibrato=0.03, release=0.5)), 0.34)
    write("BossHit.wav", mix(tone(0.12, 400, 150, "square", attack=0.001, release=1.0, gain=0.6), tone(0.1, 1800, 400, "noise", release=1.3, gain=0.4)), 0.28)
    write("Plate.wav", mix(tone(0.25, 1600, 300, "noise", attack=0.001, release=1.0), tone(0.2, 900, 200, "square", release=0.9, gain=0.5)), 0.32)
    write("BossDie.wav", cat(*[mix(tone(0.35, 2400 - i * 300, 120, "noise", attack=0.001, release=0.7), tone(0.3, 200 - i * 20, 40, "saw", release=0.7, gain=0.5)) for i in range(5)],
                             mix(tone(1.6, 2600, 50, "noise", attack=0.002, release=0.4), tone(1.4, 120, 25, "saw", vibrato=0.3, vib_rate=8, release=0.5, gain=0.7))), 0.5)
    write("BossLeave.wav", tone(1.0, 300, 1600, "saw", attack=0.05, vibrato=0.05, vib_rate=8, release=0.5), 0.24)
    # The computer.
    for name, words in LINES.items():
        write(f"{name}.wav", computer(words), 0.75)

    # The stage tune (an original composition): a driving, hopeful line in D minor over a bouncing bass, looping.
    lead = [("D5", 1), ("F5", 0.5), ("A5", 1), ("G5", 0.5), ("F5", 0.5), ("E5", 0.5),
            ("F5", 1), ("D5", 0.5), ("C5", 1.5), ("A4", 1),
            ("A#4", 1), ("D5", 0.5), ("F5", 1), ("E5", 0.5), ("D5", 0.5), ("C5", 0.5),
            ("D5", 1), ("E5", 0.5), ("C#5", 1.5), ("A4", 1),
            ("D5", 1), ("F5", 0.5), ("A5", 1), ("C6", 0.5), ("A#5", 0.5), ("A5", 0.5),
            ("G5", 1), ("A#5", 0.5), ("A5", 1.5), ("F5", 1),
            ("G5", 1), ("F5", 0.5), ("E5", 1), ("D5", 0.5), ("C#5", 0.5), ("E5", 0.5),
            ("D5", 3), ("-", 1)]
    bass = bounce("D2", 4) + bounce("A#1", 4) + bounce("G1", 4) + bounce("A1", 4) + bounce("D2", 4) + bounce("F2", 4) + bounce("G1", 4) + bounce("A1", 2) + bounce("D2", 2)
    write("Music.wav", tune(144, lead, bass, kind="pulse"), 0.26)
    # The boss: a low, insistent figure and a stabbing line.
    blead = [("-", 2), ("A4", 0.5), ("G#4", 0.5), ("A4", 0.5), ("C5", 0.5), ("-", 2), ("A4", 0.5), ("G#4", 0.5), ("A4", 0.5), ("D#5", 0.5),
             ("-", 2), ("E5", 0.5), ("D#5", 0.5), ("E5", 0.5), ("G5", 0.5), ("F5", 1), ("E5", 1), ("D#5", 1), ("E5", 1)]
    bbass = [("A1", 0.25), ("A1", 0.25), ("A2", 0.25), ("A1", 0.25)] * 8 + [("F1", 0.25), ("F1", 0.25), ("F2", 0.25), ("F1", 0.25)] * 4 + [("E1", 0.25), ("E1", 0.25), ("E2", 0.25), ("E1", 0.25)] * 4
    write("BossMusic.wav", tune(150, blead, bbass, kind="saw"), 0.28)
    title_lead = [("D5", 1), ("A4", 0.5), ("D5", 0.5), ("F5", 1), ("E5", 0.5), ("D5", 0.5), ("E5", 1), ("C5", 1), ("A4", 2),
                  ("A#4", 1), ("C5", 0.5), ("D5", 0.5), ("F5", 1), ("G5", 0.5), ("A5", 0.5), ("A5", 1.5), ("G5", 0.5), ("E5", 1), ("C#5", 1),
                  ("D5", 4)]
    title_bass = [("D2", 2), ("D2", 2), ("C2", 2), ("A1", 2), ("A#1", 2), ("F2", 2), ("A1", 2), ("A1", 2), ("D2", 4)]
    write("TitleTune.wav", cat(tune(110, title_lead, title_bass, kind="pulse"), silence(0.8)), 0.38)
    write("StageClear.wav", tune(150, [("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 2)], [("D2", 1), ("A1", 1), ("D2", 2)], kind="pulse"), 0.36)
    write("Victory.wav", tune(120, [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 1.5), ("C#6", 0.5), ("D6", 0.5), ("E6", 0.5), ("F#6", 3)],
                              [("D2", 1), ("A1", 1), ("B1", 1), ("G1", 1), ("A1", 1), ("D2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("A4", 1), ("F4", 1), ("E4", 1), ("C#4", 1), ("D4", 4)], [("D2", 2), ("A#1", 2), ("A1", 2), ("D1", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
