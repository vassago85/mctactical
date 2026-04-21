<script setup lang="ts">
defineProps<{
  title?: string
  description?: string
  /** Layout: single column (default) or 2-col grid on wider screens. */
  columns?: 1 | 2
}>()
</script>

<template>
  <section class="mc-form-section">
    <header v-if="title || description" class="mc-form-section__head">
      <h3 v-if="title" class="mc-form-section__title">{{ title }}</h3>
      <p v-if="description" class="mc-form-section__desc">{{ description }}</p>
    </header>
    <div class="mc-form-section__grid" :class="`mc-form-section__grid--${columns ?? 1}`">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.mc-form-section {
  display: flex;
  flex-direction: column;
  gap: 0.8rem;
  padding: 1.2rem 1.3rem;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 14px;
}
.mc-form-section__head {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}
.mc-form-section__title {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
  color: var(--mc-app-text, #1a1a1c);
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  letter-spacing: 0.05em;
  text-transform: uppercase;
}
.mc-form-section__desc {
  margin: 0;
  color: var(--mc-app-text-muted, #5c5a56);
  font-size: 0.88rem;
}
.mc-form-section__grid {
  display: grid;
  gap: 0.75rem;
}
.mc-form-section__grid--1 { grid-template-columns: 1fr; }
.mc-form-section__grid--2 { grid-template-columns: repeat(2, minmax(0, 1fr)); }

@media (max-width: 640px) {
  .mc-form-section { padding: 1rem; }
  .mc-form-section__grid--2 { grid-template-columns: 1fr; }
}
</style>
