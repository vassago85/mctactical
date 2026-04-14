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
  gap: 0.4rem;
  min-height: 44px;
  padding: 0 1.25rem;
  border-radius: var(--mc-app-radius-control, 10px);
  font: inherit;
  font-weight: 700;
  font-size: 0.84rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  cursor: pointer;
  border: 1.5px solid transparent;
  transition:
    filter 0.15s ease,
    background 0.15s ease,
    border-color 0.15s ease,
    box-shadow 0.15s ease,
    color 0.15s ease;
}
.mc-btn:disabled {
  cursor: not-allowed;
  pointer-events: none;
}
.mc-btn--primary:disabled {
  background: #d4b599;
  border-color: transparent;
  color: rgba(10, 10, 11, 0.5);
  box-shadow: none;
}
.mc-btn--secondary:disabled {
  background: var(--mc-app-surface-muted, #f0eeea);
  border-color: var(--mc-app-border-faint, #eceae5);
  color: var(--mc-app-border, #9e9c94);
  box-shadow: none;
}
.mc-btn--danger:disabled {
  background: #c4a0a0;
  border-color: transparent;
  color: rgba(255, 255, 255, 0.6);
  box-shadow: none;
}
.mc-btn--ghost:disabled {
  color: var(--mc-app-border-subtle, #c8c5bd);
}
.mc-btn--block {
  width: 100%;
  max-width: 100%;
}
.mc-btn--dense {
  min-height: 36px;
  padding: 0 0.8rem;
  font-size: 0.6875rem;
  letter-spacing: 0.055em;
  border-radius: 8px;
}
.mc-btn--primary {
  background: linear-gradient(180deg, #ff9348 0%, #f47a20 45%, #d96516 100%);
  border-color: rgba(0,0,0,0.12);
  color: #0a0a0b;
  box-shadow: 0 1px 3px rgba(244, 122, 32, 0.3), inset 0 1px 0 rgba(255,255,255,0.2);
}
.mc-btn--primary:hover:not(:disabled) {
  filter: brightness(1.06);
  box-shadow: 0 2px 8px rgba(244, 122, 32, 0.35), inset 0 1px 0 rgba(255,255,255,0.2);
}
.mc-btn--secondary {
  background: var(--mc-app-surface, #fff);
  border-color: var(--mc-app-border-subtle, #c8c5bd);
  color: var(--mc-app-text-secondary, #333336);
  box-shadow: 0 1px 2px rgba(0,0,0,0.05);
}
.mc-btn--secondary:hover:not(:disabled) {
  background: var(--mc-app-surface-muted, #f0eeea);
  border-color: var(--mc-app-border, #9e9c94);
  box-shadow: 0 1px 3px rgba(0,0,0,0.08);
}
.mc-btn--danger {
  background: linear-gradient(180deg, #c0392b 0%, #9a1818 100%);
  border-color: rgba(0,0,0,0.15);
  color: #fff;
  box-shadow: 0 1px 3px rgba(154, 24, 24, 0.3);
}
.mc-btn--danger:hover:not(:disabled) {
  filter: brightness(1.08);
  box-shadow: 0 2px 8px rgba(154, 24, 24, 0.35);
}
.mc-btn--ghost {
  background: transparent;
  border-color: transparent;
  color: var(--mc-app-text-muted, #5c5a56);
}
.mc-btn--ghost:hover:not(:disabled) {
  background: rgba(18, 18, 20, 0.06);
  color: var(--mc-app-text, #1a1a1c);
}
.mc-btn--ghost.mc-btn--dense {
  border-color: var(--mc-app-border-faint, #eceae5);
}
.mc-btn--ghost.mc-btn--dense:hover:not(:disabled) {
  border-color: var(--mc-app-border-soft, #ddd9d3);
}
</style>
