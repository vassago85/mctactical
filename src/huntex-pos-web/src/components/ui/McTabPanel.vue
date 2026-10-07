<script setup lang="ts">
import { provide } from 'vue'
import { EMBEDDED_PAGE } from './embedded'

provide(EMBEDDED_PAGE, true)
</script>

<template>
  <div class="mc-tab-panel">
    <slot />
  </div>
</template>
