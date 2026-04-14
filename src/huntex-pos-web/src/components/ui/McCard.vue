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
  background: #fff;
  border: 1px solid #e2e0db;
  border-radius: 12px;
  box-shadow: 0 1px 3px rgba(26, 26, 28, 0.06);
  margin-bottom: 1.25rem;
  overflow: hidden;
}
.mc-card--tight .mc-card__body {
  padding: 0;
}
.mc-card__head {
  padding: 0.9rem 1.15rem;
  border-bottom: 1px solid #eceae6;
  background: linear-gradient(180deg, #fafaf8 0%, #fff 100%);
}
.mc-card__title {
  margin: 0;
  font-family: 'Barlow Condensed', 'Arial Narrow', sans-serif;
  font-size: 1.05rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: #2a2a2d;
}
.mc-card__body {
  padding: 1.15rem;
}
.mc-card--tight .mc-card__head + .mc-card__body {
  padding: 0;
}
</style>
