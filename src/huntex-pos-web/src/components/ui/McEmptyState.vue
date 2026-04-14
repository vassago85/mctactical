<script setup lang="ts">
defineProps<{
  title: string
  hint?: string
}>()
</script>

<template>
  <div class="mc-empty">
    <p class="mc-empty__title">{{ title }}</p>
    <p v-if="hint" class="mc-empty__hint">{{ hint }}</p>
    <div v-if="$slots.default" class="mc-empty__actions">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.mc-empty {
  text-align: center;
  padding: 3rem 2rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.mc-empty__title {
  margin: 0 0 0.5rem;
  font-weight: 700;
  font-size: 1.1rem;
  color: var(--mc-app-text-secondary, #333336);
}
.mc-empty__hint {
  margin: 0 auto 1rem;
  max-width: 38ch;
  font-size: 0.9rem;
  line-height: 1.5;
  font-weight: 500;
}
.mc-empty__actions {
  display: flex;
  justify-content: center;
  flex-wrap: wrap;
  gap: 0.5rem;
}
</style>
