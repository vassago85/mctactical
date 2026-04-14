<script setup lang="ts">
import { useToast } from '@/composables/useToast'

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
        <button type="button" class="mc-toast__close" aria-label="Dismiss" @click="dismiss(t.id)">×</button>
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
  border-radius: 10px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
  font-size: 0.95rem;
  font-weight: 500;
  border: 1px solid var(--toast-border, #ddd);
  background: var(--toast-bg, #fff);
  color: var(--toast-fg, #1a1a1c);
}
.mc-toast--success {
  --toast-bg: #e8f5e9;
  --toast-border: #a5d6a7;
  --toast-fg: #1b5e20;
}
.mc-toast--error {
  --toast-bg: #ffebee;
  --toast-border: #ef9a9a;
  --toast-fg: #b71c1c;
}
.mc-toast--info {
  --toast-bg: #fff8f3;
  --toast-border: #ffcc80;
  --toast-fg: #5d4037;
}
.mc-toast__msg {
  flex: 1;
  white-space: pre-wrap;
  line-height: 1.4;
}
.mc-toast__close {
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
