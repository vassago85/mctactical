<script setup lang="ts">
defineProps<{
  title?: string
  /** When true, the panel sticks to the top on wide screens (useful for cart totals). */
  sticky?: boolean
}>()
</script>

<template>
  <aside class="mc-summary" :class="{ 'mc-summary--sticky': sticky }">
    <header v-if="title || $slots.header" class="mc-summary__head">
      <slot name="header">
        <h3 class="mc-summary__title">{{ title }}</h3>
      </slot>
    </header>
    <div class="mc-summary__body">
      <slot />
    </div>
    <footer v-if="$slots.footer" class="mc-summary__foot">
      <slot name="footer" />
    </footer>
  </aside>
</template>

<style scoped>
.mc-summary {
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: var(--mc-app-radius-card, 18px);
  box-shadow: var(--mc-app-shadow-xs, 0 1px 2px rgba(0,0,0,0.06));
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.mc-summary--sticky {
  position: sticky;
  top: 1rem;
}
.mc-summary__head {
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface-2, #f9f8f6);
}
.mc-summary__title {
  margin: 0;
  font-size: 0.85rem;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  color: var(--mc-app-text-secondary, #333336);
}
.mc-summary__body {
  padding: 1rem 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.mc-summary__foot {
  padding: 0.85rem 1.25rem;
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface-2, #f9f8f6);
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
</style>
