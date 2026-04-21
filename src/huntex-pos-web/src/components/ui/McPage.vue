<script setup lang="ts">
defineProps<{
  /** Max container width. Defaults to "lg". */
  width?: 'sm' | 'md' | 'lg' | 'xl' | 'full'
  /** Removes outer padding when you need an edge-to-edge page. */
  flush?: boolean
}>()
</script>

<template>
  <div
    class="mc-page"
    :class="[
      `mc-page--${width ?? 'lg'}`,
      { 'mc-page--flush': flush }
    ]"
  >
    <slot />
  </div>
</template>

<style scoped>
.mc-page {
  width: 100%;
  margin-inline: auto;
  padding: var(--mc-app-pad-page-y, 1.5rem) var(--mc-app-pad-page-x, 2rem);
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}
.mc-page--flush {
  padding: 0;
}
.mc-page--sm { max-width: var(--mc-container-sm); }
.mc-page--md { max-width: var(--mc-container-md); }
.mc-page--lg { max-width: var(--mc-container-lg); }
.mc-page--xl { max-width: var(--mc-container-xl); }
.mc-page--full { max-width: none; }

@media (max-width: 720px) {
  .mc-page {
    padding: 1rem;
    gap: 1rem;
  }
}
</style>
