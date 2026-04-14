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
  gap: 1rem;
  margin-bottom: 1.5rem;
}
.mc-page-header__title {
  margin: 0 0 0.35rem;
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  font-size: clamp(1.5rem, 3vw, 2rem);
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: #1a1a1c;
  line-height: 1.1;
}
.mc-page-header__desc {
  margin: 0;
  max-width: 52ch;
  font-size: 0.95rem;
  color: #5c5a56;
  line-height: 1.45;
}
.mc-page-header__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
}
</style>
