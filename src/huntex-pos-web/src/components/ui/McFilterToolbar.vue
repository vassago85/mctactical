<script setup lang="ts">
withDefaults(
  defineProps<{
    sticky?: boolean
    dense?: boolean
  }>(),
  { sticky: false, dense: false }
)
</script>

<template>
  <div
    class="mc-filter-toolbar"
    :class="{
      'mc-filter-toolbar--sticky': sticky,
      'mc-filter-toolbar--dense': dense
    }"
  >
    <div class="mc-filter-toolbar__main">
      <slot />
    </div>
    <div v-if="$slots.actions" class="mc-filter-toolbar__actions">
      <slot name="actions" />
    </div>
  </div>
</template>

<style scoped>
.mc-filter-toolbar {
  background: var(--mc-app-surface, #ffffff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: var(--mc-app-radius-card, 14px);
  padding: 0.75rem 0.9rem;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-bottom: 1rem;
  box-shadow: var(--mc-app-shadow-sm, 0 1px 3px rgba(0, 0, 0, 0.04));
}
.mc-filter-toolbar--sticky {
  position: sticky;
  top: 0;
  z-index: 4;
}
.mc-filter-toolbar--dense {
  padding: 0.5rem 0.75rem;
}
.mc-filter-toolbar__main {
  display: flex;
  align-items: center;
  gap: 0.65rem;
  flex-wrap: wrap;
  flex: 1;
  min-width: 0;
}
.mc-filter-toolbar__actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-left: auto;
  flex-wrap: wrap;
}

.mc-filter-toolbar :deep(input[type='text']),
.mc-filter-toolbar :deep(input[type='search']),
.mc-filter-toolbar :deep(input[type='number']),
.mc-filter-toolbar :deep(select) {
  height: 36px;
  padding: 0 0.65rem;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 8px;
  background: var(--mc-app-surface, #fff);
  font-size: 0.9rem;
  color: var(--mc-app-text, #0a0a0c);
  min-width: 0;
}
.mc-filter-toolbar :deep(input[type='search']:focus),
.mc-filter-toolbar :deep(input[type='text']:focus),
.mc-filter-toolbar :deep(select:focus) {
  outline: 2px solid rgba(244, 122, 32, 0.35);
  outline-offset: 1px;
  border-color: rgba(244, 122, 32, 0.55);
}

@media (max-width: 720px) {
  .mc-filter-toolbar {
    padding: 0.65rem 0.75rem;
  }
  .mc-filter-toolbar__actions {
    margin-left: 0;
    width: 100%;
  }
}
</style>
