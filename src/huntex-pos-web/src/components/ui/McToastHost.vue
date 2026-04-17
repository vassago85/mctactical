<script setup lang="ts">
import { useToast } from '@/composables/useToast'
import { X } from 'lucide-vue-next'

const { toasts, dismiss } = useToast()
</script>

<template>
  <div class="mc-toast-host" aria-live="polite">
    <TransitionGroup name="mc-toast">
      <div
        v-for="t in toasts"
        :key="t.id"
        class="mc-toast"
        :class="`mc-toast--${t.type}`"
        role="status"
      >
        <span class="mc-toast__msg">{{ t.message }}</span>
        <button type="button" class="mc-toast__close" aria-label="Dismiss" @click="dismiss(t.id)"><X :size="16" /></button>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.mc-toast-host {
  position: fixed;
  z-index: 10050;
  bottom: 1.25rem;
  right: 1.25rem;
  left: 1.25rem;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.5rem;
  pointer-events: none;
}
.mc-toast {
  pointer-events: auto;
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  max-width: min(420px, 100%);
  padding: 0.85rem 1rem;
  border-radius: var(--mc-app-radius-control, 10px);
  box-shadow: 0 10px 36px rgba(0, 0, 0, 0.22);
  font-size: 0.9375rem;
  font-weight: 600;
  border: 1px solid var(--toast-border, #b5b3ab);
  background: var(--toast-bg, #fff);
  color: var(--toast-fg, #121214);
}
.mc-toast--success {
  --toast-bg: #d9e8da;
  --toast-border: #6a9a6e;
  --toast-fg: #0f2912;
}
.mc-toast--error {
  --toast-bg: #f2dede;
  --toast-border: #c08080;
  --toast-fg: #5c1010;
}
.mc-toast--info {
  --toast-bg: #f5ebe3;
  --toast-border: #d4a574;
  --toast-fg: #2a1a0f;
}
.mc-toast__msg {
  flex: 1;
  white-space: pre-wrap;
  line-height: 1.4;
}
.mc-toast__close {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 2rem;
  height: 2rem;
  border: none;
  background: transparent;
  color: inherit;
  opacity: 0.7;
  font-size: 1.25rem;
  line-height: 1;
  cursor: pointer;
  border-radius: 6px;
}
.mc-toast__close:hover {
  opacity: 1;
  background: rgba(0, 0, 0, 0.06);
}
.mc-toast-enter-active,
.mc-toast-leave-active {
  transition: all 0.25s ease;
}
.mc-toast-enter-from,
.mc-toast-leave-to {
  opacity: 0;
  transform: translateY(12px);
}
</style>
