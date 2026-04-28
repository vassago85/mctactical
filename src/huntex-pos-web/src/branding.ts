/**
 * Build-time branding fallbacks.
 *
 * The runtime branding (logo, colours, business name) lives in
 * `BusinessSettings` on the server and is fetched via `useBranding()`.
 * These exports exist only as last-resort fallbacks for the rare moments
 * before that fetch resolves, or when an operator hasn't uploaded a logo.
 *
 * Both are deliberately null in the white-label build — consumers should
 * render a text wordmark using `businessName` when these are null.
 */
export const logoLight: string | null = null
export const logoDark: string | null = null
