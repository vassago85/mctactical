<script setup lang="ts">
defineProps<{
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost'
  type?: 'button' | 'submit'
  disabled?: boolean
  block?: boolean
  /** Tighter control for tables and dense toolbars */
  dense?: boolean
}>()
</script>

<template>
  <button
    :type="type ?? 'button'"
    class="mc-btn"
    :class="[
      `mc-btn--${variant ?? 'primary'}`,
      { 'mc-btn--block': block, 'mc-btn--dense': dense }
    ]"
    :disabled="disabled"
  >
    <slot />
  </button>
</template>

<style scoped>
.mc-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.35rem;
  min-height: 44px;
  padding: 0 1.15rem;
  border-radius: var(--mc-app-radius-control, 8px);
  font: inherit;
  font-weight: 600;
  font-size: 0.875rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  cursor: pointer;
  border: 1px solid transparent;
  transition:
    filter 0.15s ease,
    background 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
}
.mc-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}
.mc-btn--block {
  width: 100%;
}
.mc-btn--dense {
  min-height: 36px;
  padding: 0 0.75rem;
  font-size: 0.6875rem;
  letter-spacing: 0.055em;
  border-radius: 7px;
}
.mc-btn--primary {
  background: linear-gradient(180deg, #ff9348 0%, #f47a20 45%, #d96516 100%);
  border-color: #a34a0a;
  color: #0a0a0b;
}
.mc-btn--primary:hover:not(:disabled) {
  filter: brightness(1.05);
}
.mc-btn--secondary {
  background: var(--mc-app-surface, #fff);
  border-color: var(--mc-app-border-subtle, #b5b3ab);
  color: var(--mc-app-text-secondary, #2c2c30);
}
.mc-btn--secondary:hover:not(:disabled) {
  background: var(--mc-app-surface-muted, #ebe9e4);
  border-color: var(--mc-app-border, #9e9c94);
}
.mc-btn--danger {
  background: #9a1818;
  border-color: #6b0f0f;
  color: #fff;
}
.mc-btn--danger:hover:not(:disabled) {
  filter: brightness(1.06);
}
.mc-btn--ghost {
  background: transparent;
  border-color: transparent;
  color: var(--mc-app-text-muted, #4a4842);
}
.mc-btn--ghost:hover:not(:disabled) {
  background: rgba(18, 18, 20, 0.06);
  color: var(--mc-app-text, #121214);
}
.mc-btn--ghost.mc-btn--dense {
  border-color: var(--mc-app-border-faint, #e0ded8);
}
.mc-btn--ghost.mc-btn--dense:hover:not(:disabled) {
  border-color: var(--mc-app-border-soft, #cfcdc6);
}
</style>
