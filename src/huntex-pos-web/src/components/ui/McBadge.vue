<script setup lang="ts">
defineProps<{
  variant?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger'
}>()
</script>

<template>
  <span class="mc-badge" :class="`mc-badge--${variant ?? 'neutral'}`">
    <slot />
  </span>
</template>

<style scoped>
.mc-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.22rem 0.55rem;
  border-radius: 6px;
  font-size: 0.6875rem;
  font-weight: 700;
  letter-spacing: 0.055em;
  text-transform: uppercase;
  border: 1px solid transparent;
  line-height: 1.2;
}
.mc-badge--neutral {
  background: var(--mc-app-surface-muted, #ebe9e4);
  border-color: var(--mc-app-border-soft, #cfcdc6);
  color: var(--mc-app-text-secondary, #2c2c30);
}
.mc-badge--accent {
  background: rgba(244, 122, 32, 0.12);
  border-color: rgba(163, 74, 10, 0.45);
  color: #7a3a08;
}
.mc-badge--success {
  background: #c5d4c6;
  border-color: #5a7a5c;
  color: #0d280f;
}
.mc-badge--warning {
  background: #ede4cf;
  border-color: #c4a01a;
  color: #5c4200;
}
.mc-badge--danger {
  background: #e8d0d0;
  border-color: #a85a5a;
  color: #5c1010;
}
</style>
