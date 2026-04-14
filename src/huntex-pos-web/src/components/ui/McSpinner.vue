<template>
  <span class="mc-spinner" aria-hidden="true" />
</template>

<style scoped>
.mc-spinner {
  display: inline-block;
  width: 1.25rem;
  height: 1.25rem;
  border: 2px solid rgba(244, 122, 32, 0.25);
  border-top-color: #f47a20;
  border-radius: 50%;
  animation: mc-spin 0.7s linear infinite;
  vertical-align: middle;
}
@keyframes mc-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
