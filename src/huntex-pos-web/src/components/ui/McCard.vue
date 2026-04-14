<script setup lang="ts">
defineProps<{
  padded?: boolean
  title?: string
}>()
</script>

<template>
  <section class="mc-card" :class="{ 'mc-card--tight': padded === false }">
    <header v-if="title || $slots.header" class="mc-card__head">
      <slot name="header">
        <h2 class="mc-card__title">{{ title }}</h2>
      </slot>
    </header>
    <div class="mc-card__body">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.mc-card {
  background: var(--mc-app-surface, #ffffff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: var(--mc-app-radius-card, 18px);
  box-shadow: var(--mc-app-shadow-sm, 0 2px 8px rgba(0, 0, 0, 0.08));
  margin-bottom: 1.5rem;
  max-width: 100%;
  min-width: 0;
}
.mc-card--tight .mc-card__body {
  padding: 0;
  overflow: hidden;
}
.mc-card--tight .mc-card__body > :last-child {
  border-radius: 0 0 var(--mc-app-radius-card, 18px) var(--mc-app-radius-card, 18px);
}
.mc-card__head {
  padding: 1.1rem 24px;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface-2, #f9f8f6);
  border-radius: var(--mc-app-radius-card, 18px) var(--mc-app-radius-card, 18px) 0 0;
}
.mc-card__title {
  margin: 0;
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  font-size: 0.9375rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--mc-app-heading, #0a0a0c);
}
.mc-card__body {
  padding: 24px;
  min-width: 0;
  overflow: hidden;
}
.mc-card--tight .mc-card__head + .mc-card__body {
  padding: 0;
}
</style>
