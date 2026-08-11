type ApiErrorBody = {
  error?: string
  detail?: string
  title?: string
  message?: string
  errors?: Record<string, string[] | string>
}

type ErrorLike = {
  response?: { data?: ApiErrorBody | string; status?: number }
  message?: string
}

/**
 * Pulls a human-readable message out of an API failure. The backend returns a few
 * different shapes: BadRequest bodies use `{ error }`, unhandled 500s use
 * `{ title, detail }`, and ASP.NET model-validation returns `{ errors: { field: [...] } }`.
 * Reading only `error` (the old behaviour) silently swallowed the last two, which is
 * why real failures collapsed into generic "Could not save…" toasts.
 */
export function extractApiError(e: unknown): string | null {
  const err = e as ErrorLike
  const data = err?.response?.data

  if (typeof data === 'string' && data.trim()) return data.trim()

  if (data && typeof data === 'object') {
    if (data.error) return data.error
    if (data.detail) return data.detail

    if (data.errors && typeof data.errors === 'object') {
      const messages = Object.values(data.errors)
        .flatMap((v) => (Array.isArray(v) ? v : [v]))
        .filter((v): v is string => typeof v === 'string' && v.length > 0)
      if (messages.length) return messages.join(' ')
    }

    if (data.title) return data.title
    if (data.message) return data.message
  }

  if (err?.message) return err.message
  return null
}
