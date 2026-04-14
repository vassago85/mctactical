<script setup lang="ts">
defineProps<{
  title: string
  description?: string
}>()
</script>

<template>
  <header class="mc-page-header">
    <div class="mc-page-header__text">
      <h1 class="mc-page-header__title">{{ title }}</h1>
      <p v-if="description || $slots.default" class="mc-page-header__desc">
        <slot>{{ description }}</slot>
      </p>
    </div>
    <div v-if="$slots.actions" class="mc-page-header__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<style scoped>
.mc-page-header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem 1.5rem;
  margin-bottom: 2rem;
  padding-bottom: 1.25rem;
  border-bottom: 2px solid var(--mc-app-border-faint, #eceae5);
}
.mc-page-header__title {
  margin: 0 0 0.35rem;
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  font-size: clamp(1.75rem, 3.2vw, 2.2rem);
  font-weight: 700;
  letter-spacing: 0.045em;
  text-transform: uppercase;
  color: var(--mc-app-heading, #0a0a0c);
  line-height: 1.08;
}
.mc-page-header__desc {
  margin: 0;
  max-width: 58ch;
  font-size: 0.9375rem;
  color: var(--mc-app-text-muted, #5c5a56);
  line-height: 1.55;
  font-weight: 500;
}
.mc-page-header__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  align-items: center;
}
</style>
