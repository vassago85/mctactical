<script setup lang="ts">
defineProps<{
  title?: string
  description?: string
}>()
</script>

<template>
  <section class="mc-page-section">
    <header v-if="title || description || $slots.actions || $slots.header" class="mc-page-section__head">
      <div class="mc-page-section__text">
        <h2 v-if="title" class="mc-page-section__title">{{ title }}</h2>
        <p v-if="description" class="mc-page-section__desc">{{ description }}</p>
        <slot name="header" />
      </div>
      <div v-if="$slots.actions" class="mc-page-section__actions">
        <slot name="actions" />
      </div>
    </header>
    <div class="mc-page-section__body">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.mc-page-section {
  display: flex;
  flex-direction: column;
  gap: 0.9rem;
}
.mc-page-section__head {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  gap: 1rem;
  flex-wrap: wrap;
}
.mc-page-section__text {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  min-width: 0;
  flex: 1 1 auto;
}
.mc-page-section__title {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--mc-app-heading, #0a0a0c);
}
.mc-page-section__desc {
  margin: 0;
  color: var(--mc-app-text-muted, #5c5a56);
  font-size: 0.9rem;
}
.mc-page-section__actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}
.mc-page-section__body {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
</style>
