<script setup lang="ts">
defineProps<{
  label?: string
  hint?: string
  forId?: string
}>()
</script>

<template>
  <div class="mc-field">
    <label v-if="label" class="mc-field__label" :for="forId">{{ label }}</label>
    <div class="mc-field__control">
      <slot />
    </div>
    <p v-if="hint" class="mc-field__hint">{{ hint }}</p>
  </div>
</template>

<style scoped>
.mc-field {
  margin-bottom: 1rem;
}
.mc-field__label {
  display: block;
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--mc-app-text-secondary, #333336);
  margin-bottom: 0.45rem;
  letter-spacing: 0.02em;
}
.mc-field__control :deep(input),
.mc-field__control :deep(select),
.mc-field__control :deep(textarea) {
  width: 100%;
  max-width: 100%;
  min-height: 46px;
  padding: 0.6rem 0.85rem;
  border-radius: var(--mc-app-radius-control, 10px);
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text, #1a1a1c);
  font: inherit;
  font-size: 1rem;
  box-sizing: border-box;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}
.mc-field__control :deep(textarea) {
  min-height: 88px;
  resize: vertical;
}
.mc-field__control :deep(input:hover),
.mc-field__control :deep(select:hover),
.mc-field__control :deep(textarea:hover) {
  border-color: var(--mc-app-border, #9e9c94);
}
.mc-field__control :deep(input:focus),
.mc-field__control :deep(select:focus),
.mc-field__control :deep(textarea:focus) {
  outline: none;
  border-color: var(--mc-accent, #f47a20);
  box-shadow: inset 0 0 0 1px var(--mc-accent, #f47a20);
}
.mc-field__hint {
  margin: 0.4rem 0 0;
  font-size: 0.8125rem;
  color: var(--mc-app-text-muted, #4a4842);
  line-height: 1.4;
}
</style>
