<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { BrowserMultiFormatReader } from '@zxing/browser'

const props = withDefaults(
  defineProps<{
    active: boolean
    /** Ignore repeats of the same code for this long, so one item can't stack lines. */
    cooldownMs?: number
  }>(),
  { cooldownMs: 2000 }
)
const emit = defineEmits<{ (e: 'decode', value: string): void }>()

/** Any two decodes closer than this are the same physical scan, whatever the code. */
const MIN_DECODE_GAP_MS = 600

const videoRef = ref<HTMLVideoElement | null>(null)
let controls: { stop: () => void } | null = null
const reader = new BrowserMultiFormatReader()
const error = ref<string | null>(null)
const lastDecoded = ref<string | null>(null)
let lastDecodedAt = 0

/**
 * ZXing fires the callback on every frame it can read, so a barcode held in view
 * decodes dozens of times per second. Without this gate the till would stack qty
 * (or fire repeat navigations) for a single scan.
 */
function shouldEmit(text: string): boolean {
  const now = Date.now()
  if (now - lastDecodedAt < MIN_DECODE_GAP_MS) return false
  if (text === lastDecoded.value && now - lastDecodedAt < props.cooldownMs) return false
  lastDecoded.value = text
  lastDecodedAt = now
  return true
}

async function start() {
  error.value = null
  const el = videoRef.value
  if (!el || !props.active) return
  try {
    controls = await reader.decodeFromVideoDevice(undefined, el, (result, err) => {
      if (result && shouldEmit(result.getText())) {
        emit('decode', result.getText())
      }
      if (err && !(err as { name?: string }).name?.includes('NotFound')) {
        /* ignore scan noise */
      }
    })
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Camera error'
  }
}

function stop() {
  if (controls) {
    controls.stop()
    controls = null
  }
  lastDecoded.value = null
  lastDecodedAt = 0
}

watch(
  () => props.active,
  async (v) => {
    stop()
    if (v) await start()
  }
)

onMounted(async () => {
  if (props.active) await start()
})

onBeforeUnmount(() => {
  stop()
})
</script>

<template>
  <div class="scanner">
    <video ref="videoRef" class="video" playsinline muted />
    <p v-if="error" class="err">{{ error }}</p>
    <p v-else-if="lastDecoded" class="hit" aria-live="polite">Scanned {{ lastDecoded }}</p>
  </div>
</template>

<style scoped>
.scanner {
  position: relative;
  width: 100%;
  max-width: 420px;
  margin: 0 auto;
}
.video {
  width: 100%;
  border-radius: 12px;
  background: #000;
  min-height: 200px;
}
.hit {
  margin: 0.35rem 0 0;
  text-align: center;
  font-size: 0.8rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--mc-app-text-muted, #5c5a56);
}
</style>
