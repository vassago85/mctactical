<script setup lang="ts">
defineProps<{
  /** Places the bar at the top of the scroll container instead of bottom. */
  position?: 'top' | 'bottom'
  /** Removes the surface background — useful when nesting in already-framed panels. */
  bare?: boolean
}>()
</script>

<template>
  <div
    class="mc-sticky-bar"
    :class="[
      `mc-sticky-bar--${position ?? 'bottom'}`,
      { 'mc-sticky-bar--bare': bare }
    ]"
  >
    <div class="mc-sticky-bar__inner">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.mc-sticky-bar {
  position: sticky;
  z-index: 5;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  box-shadow: var(--mc-app-shadow-sm, 0 2px 8px rgba(0,0,0,0.08));
}
.mc-sticky-bar--bottom {
  bottom: 0;
  border-top-left-radius: 14px;
  border-top-right-radius: 14px;
}
.mc-sticky-bar--top {
  top: 0;
  border-bottom-left-radius: 14px;
  border-bottom-right-radius: 14px;
}
.mc-sticky-bar--bare {
  background: transparent;
  border: 0;
  box-shadow: none;
}
.mc-sticky-bar__inner {
  display: flex;
  gap: 0.65rem;
  align-items: center;
  flex-wrap: wrap;
  padding: 0.75rem 1rem;
}
@media (max-width: 640px) {
  .mc-sticky-bar__inner {
    padding: 0.65rem 0.75rem;
    gap: 0.5rem;
  }
  .mc-sticky-bar__inner > * {
    flex: 1 1 45%;
    min-height: var(--mc-touch-min, 44px);
  }
}
</style>
