<script setup lang="ts">
defineProps<{
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost'
  type?: 'button' | 'submit'
  disabled?: boolean
  block?: boolean
}>()
</script>

<template>
  <button
    :type="type ?? 'button'"
    class="mc-btn"
    :class="[`mc-btn--${variant ?? 'primary'}`, { 'mc-btn--block': block }]"
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
  border-radius: 8px;
  font: inherit;
  font-weight: 600;
  font-size: 0.875rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  cursor: pointer;
  border: 1px solid transparent;
  transition: filter 0.15s ease, background 0.15s ease;
}
.mc-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}
.mc-btn--block {
  width: 100%;
}
.mc-btn--primary {
  background: linear-gradient(180deg, #ff9348 0%, #f47a20 45%, #d96516 100%);
  border-color: #c45f18;
  color: #0a0a0b;
}
.mc-btn--primary:hover:not(:disabled) {
  filter: brightness(1.06);
}
.mc-btn--secondary {
  background: #fff;
  border-color: #d4d2cd;
  color: #2a2a2d;
}
.mc-btn--secondary:hover:not(:disabled) {
  background: #f4f3f0;
}
.mc-btn--danger {
  background: #b71c1c;
  border-color: #7f0000;
  color: #fff;
}
.mc-btn--danger:hover:not(:disabled) {
  filter: brightness(1.08);
}
.mc-btn--ghost {
  background: transparent;
  border-color: transparent;
  color: #5c5a56;
}
.mc-btn--ghost:hover:not(:disabled) {
  background: rgba(0, 0, 0, 0.05);
  color: #1a1a1c;
}
</style>
