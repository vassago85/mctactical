<script setup lang="ts">
import { watch, onMounted, onUnmounted, nextTick, ref } from 'vue'
import { X } from 'lucide-vue-next'

const props = defineProps<{
  modelValue: boolean
  title?: string
  closeOnBackdrop?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [v: boolean]
}>()

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

const panel = ref<HTMLElement | null>(null)
let lastFocused: HTMLElement | null = null

function close() {
  emit('update:modelValue', false)
}

function onBackdrop() {
  if (props.closeOnBackdrop !== false) close()
}

function focusables(): HTMLElement[] {
  if (!panel.value) return []
  return Array.from(panel.value.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
    (el) => el.offsetParent !== null || el === document.activeElement
  )
}

watch(
  () => props.modelValue,
  async (open) => {
    document.body.style.overflow = open ? 'hidden' : ''
    if (open) {
      lastFocused = document.activeElement as HTMLElement | null
      await nextTick()
      const first = focusables()[0]
      ;(first ?? panel.value)?.focus()
    } else {
      lastFocused?.focus()
      lastFocused = null
    }
  }
)

function onKey(e: KeyboardEvent) {
  if (!props.modelValue) return
  if (e.key === 'Escape') {
    close()
    return
  }
  // Keep Tab inside the dialog so keyboard and screen-reader users can't land
  // on the page behind, which is still rendered under the backdrop.
  if (e.key !== 'Tab') return
  const items = focusables()
  if (!items.length) {
    e.preventDefault()
    panel.value?.focus()
    return
  }
  const first = items[0]
  const last = items[items.length - 1]
  const active = document.activeElement
  if (e.shiftKey && (active === first || !panel.value?.contains(active))) {
    e.preventDefault()
    last.focus()
  } else if (!e.shiftKey && active === last) {
    e.preventDefault()
    first.focus()
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKey)
})

onUnmounted(() => {
  document.body.style.overflow = ''
  window.removeEventListener('keydown', onKey)
})
</script>

<template>
  <Teleport to="body">
    <Transition name="mc-modal">
      <div v-if="modelValue" class="mc-modal-root" role="dialog" aria-modal="true" :aria-labelledby="title ? 'mc-modal-title' : undefined">
        <div class="mc-modal-backdrop" @click="onBackdrop" />
        <div ref="panel" class="mc-modal-panel" tabindex="-1">
          <header v-if="title || $slots.title" class="mc-modal-header">
            <slot name="title">
              <h2 id="mc-modal-title" class="mc-modal-title">{{ title }}</h2>
            </slot>
            <button type="button" class="mc-modal-x" aria-label="Close" @click="close"><X :size="18" /></button>
          </header>
          <div class="mc-modal-body">
            <slot />
          </div>
          <footer v-if="$slots.footer" class="mc-modal-footer">
            <slot name="footer" />
          </footer>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.mc-modal-root {
  position: fixed;
  inset: 0;
  z-index: 10040;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
}
.mc-modal-backdrop {
  position: absolute;
  inset: 0;
  background: rgba(10, 10, 11, 0.5);
  backdrop-filter: blur(4px);
}
.mc-modal-panel {
  position: relative;
  width: 100%;
  max-width: 480px;
  max-height: min(90dvh, 720px);
  overflow: auto;
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text, #1a1a1c);
  border-radius: 16px;
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.25), 0 8px 24px rgba(0, 0, 0, 0.1);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
}
.mc-modal-panel:focus-visible {
  outline: 2px solid var(--mc-accent, #f47a20);
  outline-offset: 2px;
}
.mc-modal-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface-2, #f9f8f6);
  border-radius: 16px 16px 0 0;
}
.mc-modal-title {
  margin: 0;
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  font-size: 1.35rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--mc-app-heading, #0a0a0c);
}
.mc-modal-x {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 2.5rem;
  height: 2.5rem;
  border: 1.5px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface, #fff);
  border-radius: 10px;
  font-size: 1.35rem;
  line-height: 1;
  cursor: pointer;
  color: var(--mc-app-text-secondary, #333336);
  transition: background 0.15s ease, border-color 0.15s ease;
}
.mc-modal-x:hover {
  background: var(--mc-app-surface-muted, #f0eeea);
  border-color: var(--mc-app-border-subtle, #c8c5bd);
}
.mc-modal-body {
  padding: 1.5rem;
}
.mc-modal-footer {
  padding: 1.15rem 1.5rem;
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  justify-content: flex-end;
  background: var(--mc-app-surface-2, #f9f8f6);
  border-radius: 0 0 16px 16px;
}
.mc-modal-enter-active,
.mc-modal-leave-active {
  transition: opacity 0.2s ease;
}
.mc-modal-enter-active .mc-modal-panel,
.mc-modal-leave-active .mc-modal-panel {
  transition: transform 0.2s ease;
}
.mc-modal-enter-from,
.mc-modal-leave-to {
  opacity: 0;
}
.mc-modal-enter-from .mc-modal-panel,
.mc-modal-leave-to .mc-modal-panel {
  transform: scale(0.96) translateY(8px);
}
</style>
