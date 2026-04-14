<script setup lang="ts">
defineProps<{
  variant?: 'error' | 'success' | 'info' | 'warning'
}>()
</script>

<template>
  <div class="mc-alert" :class="`mc-alert--${variant ?? 'info'}`" role="alert">
    <slot />
  </div>
</template>

<style scoped>
.mc-alert {
  padding: 0.9rem 1.15rem;
  border-radius: 12px;
  font-size: 0.9rem;
  line-height: 1.5;
  font-weight: 600;
  margin-bottom: 1.25rem;
  border: 1.5px solid transparent;
}
.mc-alert--error {
  background: #fef2f2;
  border-color: #f5c6c6;
  color: #7f1d1d;
}
.mc-alert--success {
  background: #f0fdf4;
  border-color: #a7d8b0;
  color: #14532d;
}
.mc-alert--info {
  background: #fef7ed;
  border-color: #f5d4a8;
  color: #7c2d12;
}
.mc-alert--warning {
  background: #fffbeb;
  border-color: #fde68a;
  color: #78350f;
}
</style>
