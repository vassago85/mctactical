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
  padding: 0.2rem 0.55rem;
  border-radius: 6px;
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}
.mc-badge--neutral {
  background: #eceae6;
  color: #3d3d40;
}
.mc-badge--accent {
  background: rgba(244, 122, 32, 0.18);
  color: #c45f18;
}
.mc-badge--success {
  background: #c8e6c9;
  color: #1b5e20;
}
.mc-badge--warning {
  background: #fff8e1;
  color: #f57f17;
}
.mc-badge--danger {
  background: #ffcdd2;
  color: #b71c1c;
}
</style>
