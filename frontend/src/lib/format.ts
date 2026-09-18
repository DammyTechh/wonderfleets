import { format, formatDistanceToNowStrict, parseISO } from 'date-fns'

const WAT = 'Africa/Lagos'

export const dt = (value?: string | null, pattern = 'dd MMM, HH:mm') =>
  value ? format(parseISO(value), pattern) : '—'

export const day = (value?: string | null) => dt(value, 'dd MMM yyyy')

export const since = (value?: string | null) =>
  value ? `${formatDistanceToNowStrict(parseISO(value))} ago` : 'no data yet'

export const temperature = (value?: number | null) => (value == null ? '—' : `${value.toFixed(1)}°C`)

export const humidity = (value?: number | null) => (value == null ? '—' : `${Math.round(value)}%`)

export const tonnes = (value?: number | null) => (value == null ? '—' : `${value.toFixed(value % 1 ? 1 : 0)} t`)

export const km = (value?: number | null) => (value == null ? '—' : `${value.toFixed(0)} km`)

export const percent = (value?: number | null, digits = 1) =>
  value == null ? '—' : `${value.toFixed(digits)}%`

export const signed = (value?: number | null, digits = 1) =>
  value == null ? null : `${value > 0 ? '+' : ''}${value.toFixed(digits)}`

export const initials = (name?: string | null) =>
  (name ?? '')
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('') || '—'

export const watLabel = () => `${format(new Date(), 'HH:mm:ss')} WAT`

export { WAT }
