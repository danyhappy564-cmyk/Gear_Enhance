#!/usr/bin/env python3
"""Synthesizes the 1.4.0 ??? / Mythic sounds (no external samples) and writes the
trimmed user charge sound to assets/audio/edited/.

Loudness is set to sit with the user-provided sounds (charge ≈ -24 dB mean, star2 ≈ -16 dB).

python3 tools/make_sounds.py [out_dir]   (default: assets/audio/synth)

Output: 48 kHz stereo PCM16 WAV, same format as src/Client/Audio. The random
generator is seeded, so running it again writes byte-identical files.
"""
import os
import sys
import wave

import numpy as np

SR = 48000
RNG = np.random.default_rng(1400)
CHARGE_SECONDS = 211766 / SR  # same frame count as charge.wav (4.411791667 s)


def t_axis(seconds):
    return np.arange(int(round(seconds * SR))) / SR


def place(buf, sig, at):
    start = int(at * SR)
    end = min(len(buf), start + len(sig))
    if end > start:
        buf[start:end] += sig[: end - start]


def lowpass(x, cutoff):
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    return np.fft.irfft(spec / (1 + (f / cutoff) ** 4), len(x))


def highpass(x, cutoff):
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    return np.fft.irfft(spec * (1 / (1 + (cutoff / np.maximum(f, 1)) ** 4)), len(x))


def bell(freq, seconds, decay=1.2, bright=1.0):
    t = t_axis(seconds)
    partials = [(1.0, 1.0), (2.0, 0.5 * bright), (2.76, 0.35 * bright), (5.40, 0.18 * bright), (8.93, 0.08 * bright)]
    out = np.zeros_like(t)
    for ratio, amp in partials:
        out += amp * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-t * decay * (1 + ratio * 0.35))
    attack = np.minimum(1, t / 0.004)
    return out * attack


def thump(freq_hi, freq_lo, seconds, decay):
    t = t_axis(seconds)
    freq = freq_lo + (freq_hi - freq_lo) * np.exp(-t * 18)
    phase = 2 * np.pi * np.cumsum(freq) / SR
    return np.sin(phase) * np.exp(-t * decay) * np.minimum(1, t / 0.002)


def reverb(stereo, seconds=2.2, wet=0.3):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    out = np.zeros((stereo.shape[0] + n, 2))
    for ch in range(2):
        ir = RNG.standard_normal(n) * np.exp(-t * 6.9 / seconds)
        ir = lowpass(ir, 6000)
        ir /= np.sqrt(np.sum(ir ** 2))
        size = stereo.shape[0] + n
        conv = np.fft.irfft(np.fft.rfft(stereo[:, ch], size) * np.fft.rfft(ir, size), size)
        out[:, ch] = conv * wet
        out[: stereo.shape[0], ch] += stereo[:, ch] * (1 - wet * 0.5)
    return out


def pan(mono, position):
    left = np.cos((position + 1) * np.pi / 4)
    right = np.sin((position + 1) * np.pi / 4)
    return np.stack([mono * left, mono * right], axis=1)


def finish(stereo, seconds=None, peak_db=-2.0, fade_out=0.08):
    if seconds is not None:
        frames = int(round(seconds * SR))
        stereo = stereo[:frames] if stereo.shape[0] >= frames else np.pad(stereo, ((0, frames - stereo.shape[0]), (0, 0)))
    stereo = np.tanh(stereo * 1.2) / np.tanh(1.2)
    fade = int(fade_out * SR)
    stereo[-fade:] *= np.linspace(1, 0, fade)[:, None]
    stereo[: int(0.002 * SR)] *= np.linspace(0, 1, int(0.002 * SR))[:, None]
    peak = np.max(np.abs(stereo))
    return stereo / peak * (10 ** (peak_db / 20)) if peak > 0 else stereo


def myth_confirm():
    """??? confirm window: dissonant low swell into one heavy thump."""
    seconds = 1.9
    t = t_axis(seconds)
    swell = np.clip(t / 1.15, 0, 1) ** 2 * np.exp(-np.maximum(0, t - 1.2) * 5)
    drone = sum(np.sin(2 * np.pi * f * t + RNG.uniform(0, 6)) / (i + 1) for i, f in enumerate([55, 58.27, 110, 116.5, 165]))
    drone = lowpass(drone, 900) * swell
    rise = lowpass(highpass(RNG.standard_normal(len(t)), 1500), 7000) * np.clip(t / 1.2, 0, 1) ** 3 * (t < 1.22)
    mono = drone * 0.6 + rise * 0.18
    place(mono, thump(140, 42, 0.7, 6) * 1.1, 1.22)
    place(mono, bell(233, 0.7, decay=3, bright=0.6) * 0.25, 1.22)
    stereo = np.stack([mono, np.roll(mono, 90)], axis=1)
    return finish(reverb(stereo, 1.8, 0.28), seconds, -8.5)


def myth_charge():
    """??? gauge: rising drone, quickening heartbeat, climbing whine; same length as charge.wav."""
    seconds = CHARGE_SECONDS
    t = t_axis(seconds)
    k = t / seconds
    freq = 48 + 34 * k ** 1.6
    phase = 2 * np.pi * np.cumsum(freq) / SR
    drone = (np.sin(phase) + 0.5 * np.sin(2 * phase) + 0.3 * np.sin(3.01 * phase)) * (0.35 + 0.65 * k)
    drone = lowpass(drone, 700 + 1800 * k[-1])
    whine_f = 700 + 1900 * k ** 2
    whine = np.sin(2 * np.pi * np.cumsum(whine_f) / SR) * (0.02 + 0.16 * k ** 2.5) * (1 + 0.3 * np.sin(2 * np.pi * 7 * t))
    air = lowpass(highpass(RNG.standard_normal(len(t)), 2500), 9000) * 0.08 * k ** 3
    mono = drone * 0.45 + whine + air
    beat, interval = 0.12, 0.95
    while beat < seconds - 0.15:
        place(mono, thump(110, 48, 0.32, 14) * 0.9, beat)
        place(mono, thump(95, 44, 0.28, 16) * 0.6, beat + 0.17 * interval / 0.95)
        beat += interval
        interval = max(0.24, interval * 0.8)
    stereo = np.stack([mono, np.roll(mono, 60)], axis=1)
    return finish(reverb(stereo, 1.4, 0.22), seconds, -14, fade_out=0.03)


def myth_success():
    """Mythic reached: impact, bright E-major bell arpeggio, swelling pad and a spray of sparkles."""
    seconds = 3.8
    t = t_axis(seconds)
    buf = np.zeros((len(t), 2))
    impact = thump(180, 36, 0.9, 4.5)
    place(buf[:, 0], impact, 0)
    place(buf[:, 1], impact, 0)
    crash = highpass(RNG.standard_normal(int(1.6 * SR)), 3000) * np.exp(-t_axis(1.6) * 3.2) * 0.35
    place(buf[:, 0], crash, 0.0)
    place(buf[:, 1], np.roll(crash, 300), 0.0)
    notes = [659.26, 830.61, 987.77, 1318.51, 1661.22, 1975.53]
    for i, f in enumerate(notes):
        st = pan(bell(f, 2.6, decay=1.1, bright=1.1) * 0.55, -0.6 + 1.2 * i / (len(notes) - 1))
        start = int((0.06 + i * 0.085) * SR)
        buf[start:start + len(st)] += st[: len(buf) - start]
    pad_t = t
    swell = np.clip((pad_t - 0.25) / 1.0, 0, 1) * np.exp(-np.maximum(0, pad_t - 2.4) * 2.2)
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5.2 * pad_t)
    pad = sum(np.sin(2 * np.pi * f * vib * pad_t) * a for f, a in [(164.81, 0.5), (246.94, 0.4), (329.63, 0.35), (415.30, 0.28), (493.88, 0.22)])
    pad = lowpass(pad, 2200) * swell * 0.55
    buf[:, 0] += pad
    buf[:, 1] += np.roll(pad, 240)
    for _ in range(34):
        at = RNG.uniform(0.35, 2.9)
        f = RNG.uniform(2400, 6200)
        st = pan(bell(f, 0.5, decay=7, bright=0.4) * RNG.uniform(0.06, 0.16), RNG.uniform(-0.9, 0.9))
        start = int(at * SR)
        buf[start:start + len(st)] += st[: len(buf) - start]
    return finish(reverb(buf, 2.6, 0.34), seconds, -6.5, fade_out=0.3)


def myth_up():
    """Mythic level up: short rising three-note chime with a few sparkles."""
    seconds = 1.5
    t = t_axis(seconds)
    buf = np.zeros((len(t), 2))
    for i, f in enumerate([987.77, 1318.51, 1661.22]):
        st = pan(bell(f, 1.2, decay=2.2) * 0.5, -0.4 + 0.4 * i)
        start = int((0.02 + i * 0.07) * SR)
        buf[start:start + len(st)] += st[: len(buf) - start]
    for _ in range(10):
        at = RNG.uniform(0.2, 0.9)
        st = pan(bell(RNG.uniform(3000, 6500), 0.35, decay=9, bright=0.3) * 0.1, RNG.uniform(-0.8, 0.8))
        start = int(at * SR)
        buf[start:start + len(st)] += st[: len(buf) - start]
    return finish(reverb(buf, 1.6, 0.3), seconds, -4.5, fade_out=0.2)


def write(path, stereo):
    pcm = (np.clip(stereo, -1, 1) * 32767).astype('<i2')
    with wave.open(path, 'wb') as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def trim_charge(src, dst, start=0.765, fade=0.005):
    """User charge.wav starts with ~0.775 s of near silence; cut it so the gauge starts with the sound."""
    with wave.open(src, 'rb') as w:
        ch, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        data = np.frombuffer(w.readframes(frames), dtype='<i2').reshape(-1, ch).astype(np.float64)
    data = data[int(round(start * rate)):]
    n = int(fade * rate)
    data[:n] *= np.linspace(0, 1, n)[:, None]
    with wave.open(dst, 'wb') as w:
        w.setnchannels(ch)
        w.setsampwidth(width)
        w.setframerate(rate)
        w.writeframes(np.clip(np.round(data), -32768, 32767).astype('<i2').tobytes())
    print(f'{dst}: {data.shape[0]} frames, {data.shape[0] / rate:.6f} s (trimmed {start} s)')


def main():
    root = os.path.join(os.path.dirname(__file__), '..')
    src = os.path.join(root, 'src', 'Client', 'Audio', 'charge.wav')
    if os.path.exists(src):
        edited = os.path.join(root, 'assets', 'audio', 'edited')
        os.makedirs(edited, exist_ok=True)
        trim_charge(src, os.path.join(edited, 'charge_trimmed.wav'))
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, 'assets', 'audio', 'synth')
    os.makedirs(out, exist_ok=True)
    for name, make in [('myth_confirm', myth_confirm), ('myth_charge', myth_charge), ('myth_success', myth_success), ('myth_up', myth_up)]:
        stereo = make()
        path = os.path.join(out, name + '.wav')
        write(path, stereo)
        print(f'{path}: {stereo.shape[0]} frames, {stereo.shape[0] / SR:.3f} s')


if __name__ == '__main__':
    main()
