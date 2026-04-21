/**
 * Tiny retail-friendly beep helper for POS add-to-cart feedback.
 *
 * - Zero deps: uses WebAudio.
 * - Lazy-creates a single AudioContext on first use.
 * - Never throws; if audio isn't allowed yet (no user gesture, or unsupported
 *   environment) it silently no-ops so the add flow is unaffected.
 * - Two short sine tones with a tight attack/release envelope to avoid clicks.
 * - Designed to be pleasant when fired rapidly during back-to-back scans.
 */

type Tone = {
  freq: number
  durationMs: number
  gain: number
}

const SUCCESS_TONES: Tone[] = [
  { freq: 880, durationMs: 40, gain: 0.09 },
  { freq: 1320, durationMs: 60, gain: 0.09 },
]

const ERROR_TONES: Tone[] = [
  { freq: 240, durationMs: 140, gain: 0.08 },
]

type Ctor = typeof AudioContext
let ctx: AudioContext | null = null

function getCtx(): AudioContext | null {
  if (typeof window === 'undefined') return null
  if (ctx) return ctx
  const w = window as unknown as { AudioContext?: Ctor; webkitAudioContext?: Ctor }
  const C = w.AudioContext ?? w.webkitAudioContext
  if (!C) return null
  try {
    ctx = new C()
    return ctx
  } catch {
    return null
  }
}

function playSequence(tones: Tone[]): void {
  const audio = getCtx()
  if (!audio) return
  // Autoplay policies: resume is a no-op if already running.
  try {
    if (audio.state === 'suspended') void audio.resume()
  } catch { /* ignore */ }

  let start = audio.currentTime
  for (const t of tones) {
    try {
      const osc = audio.createOscillator()
      const gain = audio.createGain()
      osc.type = 'sine'
      osc.frequency.value = t.freq
      const dur = t.durationMs / 1000
      // Tight AR envelope: ramp up 6ms, hold, ramp down over remainder.
      gain.gain.setValueAtTime(0, start)
      gain.gain.linearRampToValueAtTime(t.gain, start + 0.006)
      gain.gain.linearRampToValueAtTime(0, start + dur)
      osc.connect(gain).connect(audio.destination)
      osc.start(start)
      osc.stop(start + dur + 0.02)
      start += dur
    } catch {
      // Swallow — never let a beep break the add flow.
    }
  }
}

export function beepSuccess(): void {
  playSequence(SUCCESS_TONES)
}

export function beepError(): void {
  playSequence(ERROR_TONES)
}
