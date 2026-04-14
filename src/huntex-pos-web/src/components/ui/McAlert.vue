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
  padding: 0.85rem 1.05rem;
  border-radius: var(--mc-app-radius-control, 10px);
  font-size: 0.9rem;
  line-height: 1.45;
  font-weight: 500;
  margin-bottom: 1rem;
  border: 1px solid transparent;
}
.mc-alert--error {
  background: #f2dede;
  border-color: #c08080;
  color: #4a0f0f;
}
.mc-alert--success {
  background: #d9e8da;
  border-color: #6a9a6e;
  color: #0f2912;
}
.mc-alert--info {
  background: #f5ebe3;
  border-color: #d4a574;
  color: #3d2818;
}
.mc-alert--warning {
  background: #f2ead4;
  border-color: #c9a928;
  color: #4a3800;
}
</style>
