<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import McButton from '@/components/ui/McButton.vue'
import { useBranding } from '@/composables/useBranding'

// `BeforeInstallPromptEvent` is a Chromium-only event, not in the standard lib.dom.
interface BeforeInstallPromptEvent extends Event {
  readonly platforms: ReadonlyArray<string>
  readonly userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>
  prompt(): Promise<void>
}

const { businessName } = useBranding()

const STORAGE_KEY = 'mc-pos-pwa-install-dismissed'

function readDismissed(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === '1'
  } catch {
    return false
  }
}

function setDismissed() {
  try {
    localStorage.setItem(STORAGE_KEY, '1')
  } catch {
    /* ignore */
  }
}

function isStandalone(): boolean {
  return (
    window.matchMedia('(display-mode: standalone)').matches ||
    (window.navigator as Navigator & { standalone?: boolean }).standalone === true
  )
}

function isIosDevice(): boolean {
  const ua = navigator.userAgent
  return (
    /iPad|iPhone|iPod/.test(ua) ||
    (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)
  )
}

const dismissed = ref(readDismissed())
const deferredPrompt = ref<BeforeInstallPromptEvent | null>(null)
const showIosHint = ref(false)
let iosHintTimer: ReturnType<typeof setTimeout> | null = null

function clearIosTimer() {
  if (iosHintTimer != null) {
    clearTimeout(iosHintTimer)
    iosHintTimer = null
  }
}

function onBeforeInstallPrompt(e: Event) {
  if (dismissed.value || isStandalone()) return
  e.preventDefault()
  clearIosTimer()
  showIosHint.value = false
  deferredPrompt.value = e as BeforeInstallPromptEvent
}

function onAppInstalled() {
  deferredPrompt.value = null
  showIosHint.value = false
  clearIosTimer()
}

function dismiss() {
  setDismissed()
  dismissed.value = true
  deferredPrompt.value = null
  showIosHint.value = false
  clearIosTimer()
}

async function install() {
  const ev = deferredPrompt.value
  if (!ev) return
  try {
    await ev.prompt()
    await ev.userChoice
  } catch {
    /* user cancelled or prompt failed */
  } finally {
    deferredPrompt.value = null
  }
}

onMounted(() => {
  if (dismissed.value || isStandalone()) return

  window.addEventListener('beforeinstallprompt', onBeforeInstallPrompt)
  window.addEventListener('appinstalled', onAppInstalled)

  iosHintTimer = setTimeout(() => {
    if (dismissed.value || isStandalone()) return
    if (deferredPrompt.value) return
    if (isIosDevice()) showIosHint.value = true
  }, 2800)
})

onUnmounted(() => {
  window.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt)
  window.removeEventListener('appinstalled', onAppInstalled)
  clearIosTimer()
})

const showChromeBanner = () => !dismissed.value && deferredPrompt.value != null
const showIosBanner = () => !dismissed.value && showIosHint.value && deferredPrompt.value == null
</script>

<template>
  <Teleport to="body">
    <div
      v-if="showChromeBanner() || showIosBanner()"
      class="pwa-banner"
      role="region"
      aria-label="Install app"
    >
      <div class="pwa-banner__inner">
        <div class="pwa-banner__text">
          <strong class="pwa-banner__title">Install {{ businessName }} POS</strong>
          <p v-if="showChromeBanner()" class="pwa-banner__desc">
            Add this app to your device for quicker access and offline support.
          </p>
          <p v-else class="pwa-banner__desc">
            On iPhone or iPad: tap <strong>Share</strong>, then <strong>Add to Home Screen</strong>.
          </p>
        </div>
        <div class="pwa-banner__actions">
          <McButton v-if="showChromeBanner()" variant="primary" type="button" @click="install">
            Install
          </McButton>
          <McButton variant="ghost" type="button" class="pwa-banner__dismiss" @click="dismiss">
            Dismiss
          </McButton>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.pwa-banner {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  z-index: 10048;
  padding: var(--mc-space-3) var(--mc-space-4);
  padding-bottom: max(var(--mc-space-3), env(safe-area-inset-bottom));
  background: linear-gradient(180deg, rgba(10, 10, 11, 0.92) 0%, #0a0a0b 100%);
  border-top: 1px solid var(--mc-border);
  box-shadow: 0 -8px 32px rgba(0, 0, 0, 0.35);
}

.pwa-banner__inner {
  max-width: min(1400px, 100%);
  margin: 0 auto;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--mc-space-4);
}

.pwa-banner__text {
  flex: 1 1 220px;
  min-width: 0;
  color: var(--mc-text);
}

.pwa-banner__title {
  display: block;
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  font-size: 1rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  margin-bottom: 0.25rem;
}

.pwa-banner__desc {
  margin: 0;
  font-size: 0.9rem;
  color: var(--mc-muted);
  line-height: 1.45;
}

.pwa-banner__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--mc-space-2);
}

.pwa-banner__dismiss {
  color: var(--mc-muted) !important;
}

@media (min-width: 768px) {
  .pwa-banner {
    padding: var(--mc-space-4) var(--mc-space-6);
  }
}
</style>
