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
  font-size: 0.8125rem;
  font-weight: 700;
  color: var(--mc-app-text-secondary, #2c2c30);
  margin-bottom: 0.4rem;
  letter-spacing: 0.02em;
}
.mc-field__control :deep(input),
.mc-field__control :deep(select),
.mc-field__control :deep(textarea) {
  width: 100%;
  max-width: 100%;
  min-height: 44px;
  padding: 0.55rem 0.8rem;
  border-radius: var(--mc-app-radius-control, 8px);
  border: 1px solid var(--mc-app-border-subtle, #b5b3ab);
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text, #121214);
  font: inherit;
  font-size: 1rem;
  box-sizing: border-box;
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
  box-shadow: 0 0 0 2px rgba(244, 122, 32, 0.3);
}
.mc-field__hint {
  margin: 0.4rem 0 0;
  font-size: 0.8125rem;
  color: var(--mc-app-text-muted, #4a4842);
  line-height: 1.4;
}
</style>
