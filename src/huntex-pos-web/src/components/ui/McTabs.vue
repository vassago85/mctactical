<script setup lang="ts">
import { useRoute } from 'vue-router'

export type McTab = { to: string; label: string }

defineProps<{
  tabs: McTab[]
  label: string
}>()

const route = useRoute()
</script>

<template>
  <nav class="rep-tabs" :aria-label="label">
    <RouterLink
      v-for="t in tabs"
      :key="t.to"
      :to="t.to"
      class="rep-tab"
      :class="{ 'rep-tab--active': route.path === t.to }"
      :aria-current="route.path === t.to ? 'page' : undefined"
    >{{ t.label }}</RouterLink>
  </nav>
</template>

<style scoped>
.rep-tabs {
  display: flex;
  gap: 0;
  margin-bottom: 1.25rem;
  border-bottom: 2px solid var(--mc-app-border-faint, #eceae5);
  overflow-x: auto;
  scrollbar-width: none;
  -webkit-overflow-scrolling: touch;
}
.rep-tabs::-webkit-scrollbar {
  display: none;
}

/* Nested selectors so the global `.app-main a` link colour does not win. */
.rep-tabs .rep-tab {
  flex-shrink: 0;
  padding: 0.75rem 1.5rem;
  font-weight: 600;
  font-size: 0.95rem;
  color: var(--mc-app-text-muted, #5c5a56);
  text-decoration: none;
  white-space: nowrap;
  border-bottom: 3px solid transparent;
  margin-bottom: -2px;
  transition: color 0.15s, border-color 0.15s;
}

.rep-tabs .rep-tab:hover {
  color: var(--mc-app-text, #1a1a1c);
}

.rep-tabs .rep-tab.rep-tab--active {
  color: var(--mc-accent, #f47a20);
  border-bottom-color: var(--mc-accent, #f47a20);
}

@media (max-width: 640px) {
  .rep-tabs .rep-tab {
    padding: 0.7rem 1rem;
  }
}

@media print {
  .rep-tabs {
    display: none;
  }
}
</style>
