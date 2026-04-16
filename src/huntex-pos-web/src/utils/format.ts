const NBSP = '\u00A0'

export function formatZAR(n: number): string {
  const [int, dec] = n.toFixed(2).split('.')
  return `R${int.replace(/\B(?=(\d{3})+(?!\d))/g, NBSP)}.${dec}`
}

export function formatNumber(n: number): string {
  return n.toString().replace(/\B(?=(\d{3})+(?!\d))/g, NBSP)
}
