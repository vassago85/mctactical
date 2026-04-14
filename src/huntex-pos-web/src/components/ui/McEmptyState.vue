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
  padding: 2.5rem 1.5rem;
  color: #5c5a56;
}
.mc-empty__title {
  margin: 0 0 0.5rem;
  font-weight: 600;
  font-size: 1.05rem;
  color: #3d3d40;
}
.mc-empty__hint {
  margin: 0 auto 1rem;
  max-width: 36ch;
  font-size: 0.9rem;
  line-height: 1.45;
}
.mc-empty__actions {
  display: flex;
  justify-content: center;
  flex-wrap: wrap;
  gap: 0.5rem;
}
</style>
