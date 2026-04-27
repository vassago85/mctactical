<script setup lang="ts">
withDefaults(
  defineProps<{
    label: string
    value: string | number
    hint?: string
    variant?: 'default' | 'accent' | 'success' | 'warning' | 'danger' | 'muted'
    sensitive?: boolean
    size?: 'default' | 'compact'
  }>(),
  { variant: 'default', sensitive: false, size: 'default' }
)
</script>

<template>
  <div
    class="mc-metric"
    :class="[
      `mc-metric--${variant}`,
      size === 'compact' ? 'mc-metric--compact' : null
    ]"
  >
    <div class="mc-metric__label">{{ label }}</div>
    <div
      class="mc-metric__value"
      :class="{ 'sensitive-value': sensitive }"
    >{{ value }}</div>
    <div v-if="hint" class="mc-metric__hint">{{ hint }}</div>
  </div>
</template>

<style scoped>
.mc-metric {
  background: var(--mc-app-surface, #ffffff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: var(--mc-app-radius-card, 14px);
  padding: 0.95rem 1.1rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  min-width: 0;
  box-shadow: var(--mc-app-shadow-sm, 0 1px 3px rgba(0, 0, 0, 0.04));
  position: relative;
}
.mc-metric__label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: var(--mc-app-text-secondary, #5c5a56);
}
.mc-metric__value {
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  font-size: 1.95rem;
  font-weight: 700;
  line-height: 1.05;
  color: var(--mc-app-heading, #0a0a0c);
  font-variant-numeric: tabular-nums;
  letter-spacing: -0.01em;
  word-break: break-word;
}
.mc-metric__hint {
  font-size: 0.78rem;
  color: var(--mc-app-text-secondary, #7a7874);
}

.mc-metric--compact {
  padding: 0.75rem 0.9rem 0.85rem;
}
.mc-metric--compact .mc-metric__value {
  font-size: 1.5rem;
}

.mc-metric--accent {
  border-color: rgba(244, 122, 32, 0.35);
  background: linear-gradient(180deg, rgba(244, 122, 32, 0.06) 0%, var(--mc-app-surface, #fff) 100%);
}
.mc-metric--accent .mc-metric__value {
  color: #9a3c00;
}

.mc-metric--success {
  border-color: #a7f3d0;
}
.mc-metric--success .mc-metric__value { color: #065f46; }

.mc-metric--warning {
  border-color: #fde68a;
  background: linear-gradient(180deg, #fffbeb 0%, var(--mc-app-surface, #fff) 100%);
}
.mc-metric--warning .mc-metric__value { color: #92400e; }

.mc-metric--danger {
  border-color: #fecaca;
}
.mc-metric--danger .mc-metric__value { color: #991b1b; }

.mc-metric--muted {
  background: var(--mc-app-surface-2, #faf9f6);
}
.mc-metric--muted .mc-metric__value {
  color: var(--mc-app-text-secondary, #5c5a56);
}

@media (max-width: 480px) {
  .mc-metric { padding: 0.75rem 0.85rem 0.85rem; }
  .mc-metric__value { font-size: 1.5rem; }
}
</style>
