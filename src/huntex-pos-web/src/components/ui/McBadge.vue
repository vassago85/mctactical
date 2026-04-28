<script setup lang="ts">
const props = defineProps<{
  variant?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'error' | 'info'
}>()

const resolvedVariant = (): string => {
  // 'error' is accepted as an alias for 'danger' so toast-style callers
  // (success/error/info/warning) can pass their type straight through.
  // 'info' falls back to neutral styling.
  switch (props.variant) {
    case 'error': return 'danger'
    case 'info':
    case undefined: return 'neutral'
    default: return props.variant
  }
}
</script>

<template>
  <span class="mc-badge" :class="`mc-badge--${resolvedVariant()}`">
    <slot />
  </span>
</template>

<style scoped>
.mc-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.25rem 0.6rem;
  border-radius: 8px;
  font-size: 0.6875rem;
  font-weight: 700;
  letter-spacing: 0.055em;
  text-transform: uppercase;
  border: 1px solid transparent;
  line-height: 1.2;
}
.mc-badge--neutral {
  background: var(--mc-app-surface-muted, #f0eeea);
  border-color: var(--mc-app-border-soft, #ddd9d3);
  color: var(--mc-app-text-secondary, #333336);
}
.mc-badge--accent {
  background: rgba(244, 122, 32, 0.1);
  border-color: rgba(244, 122, 32, 0.25);
  color: #9a3c00;
}
.mc-badge--success {
  background: #ecfdf5;
  border-color: #a7f3d0;
  color: #065f46;
}
.mc-badge--warning {
  background: #fffbeb;
  border-color: #fde68a;
  color: #92400e;
}
.mc-badge--danger {
  background: #fef2f2;
  border-color: #fecaca;
  color: #991b1b;
}
</style>
