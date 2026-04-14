<script setup lang="ts">
defineProps<{
  lines?: number
}>()
</script>

<template>
  <div class="mc-skel" aria-hidden="true">
    <div v-for="i in (lines ?? 3)" :key="i" class="mc-skel__line" :style="{ width: i === (lines ?? 3) ? '60%' : '100%' }" />
  </div>
</template>

<style scoped>
.mc-skel {
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
  padding: 0.5rem 0;
}
.mc-skel__line {
  height: 12px;
  border-radius: 6px;
  background: linear-gradient(
    90deg,
    var(--mc-app-border-faint, #e0ded8) 0%,
    var(--mc-app-surface-muted, #ebe9e4) 50%,
    var(--mc-app-border-faint, #e0ded8) 100%
  );
  background-size: 200% 100%;
  animation: mc-skel-shimmer 1.2s ease-in-out infinite;
}
@keyframes mc-skel-shimmer {
  0% {
    background-position: 100% 0;
  }
  100% {
    background-position: -100% 0;
  }
}
</style>
