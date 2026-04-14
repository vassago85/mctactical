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
  font-weight: 600;
  color: #5c5a56;
  margin-bottom: 0.35rem;
  letter-spacing: 0.02em;
}
.mc-field__control :deep(input),
.mc-field__control :deep(select),
.mc-field__control :deep(textarea) {
  width: 100%;
  min-height: 44px;
  padding: 0.55rem 0.75rem;
  border-radius: 8px;
  border: 1px solid #d4d2cd;
  background: #fff;
  color: #1a1a1c;
  font: inherit;
  font-size: 1rem;
}
.mc-field__control :deep(textarea) {
  min-height: 88px;
  resize: vertical;
}
.mc-field__control :deep(input:focus),
.mc-field__control :deep(select:focus),
.mc-field__control :deep(textarea:focus) {
  outline: 2px solid rgba(244, 122, 32, 0.45);
  outline-offset: 1px;
  border-color: #f47a20;
}
.mc-field__hint {
  margin: 0.35rem 0 0;
  font-size: 0.8rem;
  color: #7a7874;
}
</style>
