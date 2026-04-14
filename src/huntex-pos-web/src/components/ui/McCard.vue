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
  border: 1px solid var(--mc-app-border-soft, #cfcdc6);
  border-radius: var(--mc-app-radius-card, 12px);
  box-shadow: var(--mc-app-shadow-sm, 0 1px 3px rgba(26, 26, 28, 0.08));
  margin-bottom: 1.5rem;
  max-width: 100%;
  min-width: 0;
}
.mc-card--tight .mc-card__body {
  padding: 0;
  overflow: hidden;
}
.mc-card--tight .mc-card__body > :last-child {
  border-radius: 0 0 var(--mc-app-radius-card, 12px) var(--mc-app-radius-card, 12px);
}
.mc-card__head {
  padding: 1rem var(--mc-app-pad-card, 1.5rem);
  border-bottom: 1px solid var(--mc-app-border-faint, #e0ded8);
  background: var(--mc-app-surface-2, #f7f6f3);
  border-radius: var(--mc-app-radius-card, 12px) var(--mc-app-radius-card, 12px) 0 0;
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
  padding: var(--mc-app-pad-card, 1.5rem);
  min-width: 0;
  overflow-x: auto;
}
.mc-card--tight .mc-card__head + .mc-card__body {
  padding: 0;
}
</style>
