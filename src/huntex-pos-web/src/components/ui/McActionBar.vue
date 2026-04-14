<script setup lang="ts">
defineProps<{
  hint?: string
}>()
</script>

<template>
  <div class="mc-action-bar">
    <div class="mc-action-bar__actions">
      <slot />
    </div>
    <p v-if="hint" class="mc-action-bar__hint">{{ hint }}</p>
  </div>
</template>

<style scoped>
.mc-action-bar {
  margin-top: 1.5rem;
  padding-top: 1.5rem;
  border-top: 2px solid var(--mc-app-border-faint, #eceae5);
}
.mc-action-bar__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: center;
}
.mc-action-bar__hint {
  margin: 0.75rem 0 0;
  font-size: 0.8125rem;
  color: var(--mc-app-text-muted, #5c5a56);
  line-height: 1.45;
}
</style>
