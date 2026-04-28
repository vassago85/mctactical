import { ref, type Ref } from 'vue'

export type ToastItem = { id: number; message: string; type: 'success' | 'error' | 'info' | 'warning' }

const toasts: Ref<ToastItem[]> = ref([])
let nextId = 1

export function useToast() {
  function push(message: string, type: ToastItem['type'] = 'info', durationMs = 4500) {
    const id = nextId++
    toasts.value = [...toasts.value, { id, message, type }]
    if (durationMs > 0) {
      setTimeout(() => dismiss(id), durationMs)
    }
    return id
  }

  function dismiss(id: number) {
    toasts.value = toasts.value.filter((t) => t.id !== id)
  }

  return {
    toasts,
    push,
    dismiss,
    success: (m: string) => push(m, 'success'),
    error: (m: string) => push(m, 'error'),
    info: (m: string) => push(m, 'info'),
    warning: (m: string) => push(m, 'warning')
  }
}
