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
  padding: 0.85rem 1rem;
  border-radius: 10px;
  font-size: 0.9rem;
  line-height: 1.45;
  margin-bottom: 1rem;
  border: 1px solid transparent;
}
.mc-alert--error {
  background: #ffebee;
  border-color: #ef9a9a;
  color: #7f1d1d;
}
.mc-alert--success {
  background: #e8f5e9;
  border-color: #a5d6a7;
  color: #1b5e20;
}
.mc-alert--info {
  background: #fff8f3;
  border-color: #ffcc80;
  color: #5d4037;
}
.mc-alert--warning {
  background: #fff8e1;
  border-color: #ffca28;
  color: #5d4037;
}
</style>
