<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { BrowserMultiFormatReader } from '@zxing/browser'

const props = defineProps<{ active: boolean }>()
const emit = defineEmits<{ (e: 'decode', value: string): void }>()

const videoRef = ref<HTMLVideoElement | null>(null)
let controls: { stop: () => void } | null = null
const reader = new BrowserMultiFormatReader()
const error = ref<string | null>(null)

async function start() {
  error.value = null
  const el = videoRef.value
  if (!el || !props.active) return
  try {
    controls = await reader.decodeFromVideoDevice(undefined, el, (result, err) => {
      if (result) {
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
</style>
