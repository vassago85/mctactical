<script setup lang="ts">
import { watch, onMounted, onUnmounted } from 'vue'

const props = defineProps<{
  modelValue: boolean
  title?: string
  closeOnBackdrop?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [v: boolean]
}>()

function close() {
  emit('update:modelValue', false)
}

function onBackdrop() {
  if (props.closeOnBackdrop !== false) close()
}

watch(
  () => props.modelValue,
  (open) => {
    document.body.style.overflow = open ? 'hidden' : ''
  }
)

function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape' && props.modelValue) close()
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
        <div class="mc-modal-panel">
          <header v-if="title || $slots.title" class="mc-modal-header">
            <slot name="title">
              <h2 id="mc-modal-title" class="mc-modal-title">{{ title }}</h2>
            </slot>
            <button type="button" class="mc-modal-x" aria-label="Close" @click="close">×</button>
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
  background: rgba(10, 10, 11, 0.55);
  backdrop-filter: blur(2px);
}
.mc-modal-panel {
  position: relative;
  width: 100%;
  max-width: 480px;
  max-height: min(90dvh, 720px);
  overflow: auto;
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text, #121214);
  border-radius: var(--mc-app-radius-card, 12px);
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.28);
  border: 1px solid var(--mc-app-border-soft, #cfcdc6);
}
.mc-modal-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.1rem 1.25rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #e0ded8);
  background: var(--mc-app-surface-2, #f7f6f3);
}
.mc-modal-title {
  margin: 0;
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  font-size: 1.35rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--mc-app-heading, #0a0a0c);
}
.mc-modal-x {
  flex-shrink: 0;
  width: 2.5rem;
  height: 2.5rem;
  border: 1px solid var(--mc-app-border-faint, #e0ded8);
  background: var(--mc-app-surface, #fff);
  border-radius: 8px;
  font-size: 1.35rem;
  line-height: 1;
  cursor: pointer;
  color: var(--mc-app-text-secondary, #2c2c30);
}
.mc-modal-x:hover {
  background: var(--mc-app-surface-muted, #ebe9e4);
}
.mc-modal-body {
  padding: 1.25rem;
}
.mc-modal-footer {
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--mc-app-border-faint, #e0ded8);
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: flex-end;
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
