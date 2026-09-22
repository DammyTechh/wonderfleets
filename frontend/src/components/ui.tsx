import clsx from 'clsx'
import { AlertTriangle, ArrowDownRight, ArrowUpRight, ChevronLeft, ChevronRight, Loader2, Search, X } from '@/components/icons'
import type { ReactNode } from 'react'
import { useEffect } from 'react'
import type { SensorStatus, Severity } from '@/lib/types'
import { label } from '@/lib/format'

export function Card({ className, children }: { className?: string; children: ReactNode }) {
  return <section className={clsx('card', className)}>{children}</section>
}

export function CardHeader({ title, subtitle, action }: { title: string; subtitle?: string; action?: ReactNode }) {
  return (
    <header className="flex items-center justify-between gap-4 border-b border-line px-5 py-3.5">
      <div className="min-w-0">
        <h2 className="text-[15px] leading-6">{title}</h2>
        {subtitle && <p className="text-[13px] text-ink-soft">{subtitle}</p>}
      </div>
      {action}
    </header>
  )
}

export function PageHeader({ title, subtitle, action }: { title: string; subtitle?: string; action?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-x-4 gap-y-3">
      <div className="min-w-0">
        <h1 className="text-[22px] leading-7">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-ink-soft">{subtitle}</p>}
      </div>
      {action}
    </div>
  )
}

/**
 * One reading: label, value, context. No icon tile — the label names it.
 * The value takes a status colour only when it is out of range, so problems stand out.
 * Use inside <StatStrip> to sit several readings in one panel, or alone as a card.
 */
export function StatCard({
  label, value, unit, delta, hint, tone = 'default', inStrip = false,
}: {
  /** Accepted for compatibility; readings are labelled by text, not icons. */
  icon?: ReactNode
  label: string
  value: string
  unit?: string
  delta?: { value: string; good?: boolean } | null
  hint?: string
  tone?: 'default' | 'critical' | 'warning' | 'brand'
  inStrip?: boolean
}) {
  const valueTone = { default: 'text-ink', brand: 'text-ink', critical: 'text-critical-600', warning: 'text-warning-600' }[tone]
  const body = (
    <div className={clsx('px-5 py-4', inStrip && 'bg-surface')}>
      <p className="text-[13px] text-ink-soft">{label}</p>
      <p className={clsx('tabular mt-1.5 flex items-baseline gap-1 text-[28px] font-semibold leading-9 tracking-tight', valueTone)}>
        {value}
        {unit && <span className="text-sm font-medium tracking-normal text-ink-soft">{unit}</span>}
      </p>
      {(delta || hint) && (
        <p className="mt-1 flex flex-wrap items-center gap-x-2 text-xs text-ink-soft">
          {delta && (
            <span className={clsx('inline-flex items-center gap-0.5 font-medium', delta.good === false ? 'text-critical-600' : 'text-normal-600')}>
              {/* Arrow follows the sign; colour says whether that is good. */}
              {delta.value.trim().startsWith('-') || delta.value.trim().startsWith('−') ? <ArrowDownRight size={12} /> : <ArrowUpRight size={12} />}
              {delta.value}
            </span>
          )}
          {hint && <span>{hint}</span>}
        </p>
      )}
    </div>
  )
  return inStrip ? body : <Card>{body}</Card>
}

/**
 * Several readings in one panel, split by hairlines — an instrument cluster rather than
 * a row of separate cards. The 1px gaps over a line-coloured background stay correct
 * however the cells wrap.
 */
export function StatStrip({ children, columns }: { children: ReactNode; columns: 3 | 4 | 5 }) {
  const layout = {
    3: 'grid-cols-1 sm:grid-cols-3',
    4: 'grid-cols-2 xl:grid-cols-4',
    // Two per row until there is room for all five; a lone last cell spans the row.
    5: 'grid-cols-2 xl:grid-cols-5 [&>*:last-child:nth-child(odd)]:col-span-2 xl:[&>*:last-child:nth-child(odd)]:col-span-1',
  }[columns]
  return (
    <section className="card overflow-hidden">
      <div className={clsx('grid gap-px bg-line', layout)}>{children}</div>
    </section>
  )
}

// Figma status pills: tinted fill, matching text, no border.
const statusStyles: Record<string, string> = {
  Normal: 'border-transparent bg-normal-100 text-normal-700',
  Critical: 'border-transparent bg-critical-100 text-critical-700',
  Warning: 'border-transparent bg-warning-100 text-warning-700',
  Offline: 'border-transparent bg-offline-100 text-offline-500',
  Idle: 'border-transparent bg-offline-100 text-offline-500',
  Informational: 'border-transparent bg-info-100 text-info-700',
  Active: 'border-transparent bg-normal-100 text-normal-700',
  Pending: 'border-transparent bg-warning-100 text-warning-700',
  Suspended: 'border-transparent bg-critical-100 text-critical-700',
  InTransit: 'border-transparent bg-info-100 text-info-700',
  Scheduled: 'border-transparent bg-offline-100 text-offline-500',
  Stopped: 'border-transparent bg-critical-100 text-critical-700',
  Delayed: 'border-transparent bg-warning-100 text-warning-700',
  Completed: 'border-transparent bg-normal-100 text-normal-700',
  Cancelled: 'border-transparent bg-offline-100 text-offline-500',
  Resolved: 'border-transparent bg-normal-100 text-normal-700',
  Acknowledged: 'border-transparent bg-info-100 text-info-700',
}

const dotStyles: Record<string, string> = {
  Normal: 'bg-normal-500',
  Critical: 'bg-critical-500',
  Warning: 'bg-warning-500',
  Offline: 'bg-ink-faint',
  Idle: 'bg-ink-faint',
}

export function StatusChip({ status, pulse }: { status: SensorStatus | Severity | string; pulse?: boolean }) {
  const text = label(status)
  return (
    <span className={clsx('chip', statusStyles[status] ?? 'border-line-strong bg-surface-sunken text-ink-soft')}>
      {dotStyles[status] && (
        <span className="relative flex h-2 w-2">
          {pulse && status === 'Critical' && (
            <span className={clsx('absolute inline-flex h-full w-full rounded-full opacity-60', dotStyles[status], 'animate-ping')} />
          )}
          <span className={clsx('relative inline-flex h-2 w-2 rounded-full', dotStyles[status])} />
        </span>
      )}
      {text}
    </span>
  )
}

export function Avatar({ initials, photoUrl, size = 36 }: { initials: string; photoUrl?: string | null; size?: number }) {
  if (photoUrl) {
    return <img src={photoUrl} alt="" width={size} height={size} className="rounded-full object-cover" style={{ width: size, height: size }} />
  }
  return (
    <span
      className="grid shrink-0 place-items-center rounded-full bg-brand-100 font-semibold text-brand-800"
      style={{ width: size, height: size, fontSize: size * 0.36 }}
    >
      {initials}
    </span>
  )
}

export function SearchInput({ value, onChange, placeholder = 'Search' }: { value: string; onChange: (v: string) => void; placeholder?: string }) {
  return (
    <div className="relative">
      <Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-faint" />
      <input
        className="input pl-9"
        value={value}
        placeholder={placeholder}
        onChange={(event) => onChange(event.target.value)}
        aria-label={placeholder}
      />
    </div>
  )
}

export function Table({ head, children }: { head: string[]; children: ReactNode }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[720px] border-collapse">
        <thead className="bg-surface-muted">
          <tr>{head.map((column) => <th key={column} className="th">{column}</th>)}</tr>
        </thead>
        <tbody className="divide-y divide-line">{children}</tbody>
      </table>
    </div>
  )
}

export function Pagination({ page, totalPages, totalCount, onChange }: { page: number; totalPages: number; totalCount: number; onChange: (page: number) => void }) {
  if (totalCount === 0) return null
  return (
    <div className="flex items-center justify-between gap-4 border-t border-line px-5 py-2.5 text-[13px] text-ink-soft">
      <span className="tabular">
        {totalCount} record{totalCount === 1 ? '' : 's'}
        {totalPages > 1 && <span className="ml-3">Page {page} of {totalPages}</span>}
      </span>
      <div className="flex gap-1.5">
        <button className="btn-secondary h-8 px-2.5" disabled={page <= 1} onClick={() => onChange(page - 1)} aria-label="Previous page">
          <ChevronLeft size={14} /> <span className="hidden sm:inline">Previous</span>
        </button>
        <button className="btn-secondary h-8 px-2.5" disabled={page >= totalPages} onClick={() => onChange(page + 1)} aria-label="Next page">
          <span className="hidden sm:inline">Next</span> <ChevronRight size={14} />
        </button>
      </div>
    </div>
  )
}

export function EmptyState({ icon, title, description, action }: { icon?: ReactNode; title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 px-6 py-14 text-center">
      {icon && <span className="text-ink-faint">{icon}</span>}
      <div className="max-w-sm">
        <p className="font-medium text-ink">{title}</p>
        {description && <p className="mt-1 text-[13px] text-ink-soft">{description}</p>}
      </div>
      {action}
    </div>
  )
}

export function Loading({ label = 'Loading', rows = 3 }: { label?: string; rows?: number }) {
  return (
    <div className="space-y-3 p-5" role="status" aria-label={label}>
      {Array.from({ length: rows }).map((_, index) => (
        <div key={index} className="skeleton h-12 w-full" />
      ))}
    </div>
  )
}

export function Spinner({ size = 16 }: { size?: number }) {
  return <Loader2 size={size} className="animate-spin" />
}

export function ErrorNote({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="m-5 flex items-start gap-3 rounded-md border border-critical-100 bg-critical-50 p-4 text-sm text-critical-700">
      <AlertTriangle size={18} className="mt-0.5 shrink-0" />
      <div className="flex-1">
        <p className="font-medium">{message}</p>
        {onRetry && (
          <button className="mt-2 font-medium underline underline-offset-2" onClick={onRetry}>
            Try again
          </button>
        )}
      </div>
    </div>
  )
}

export function Modal({ open, title, description, onClose, children, width = 'max-w-lg' }: {
  open: boolean
  title: string
  description?: string
  onClose: () => void
  children: ReactNode
  width?: string
}) {
  useEffect(() => {
    if (!open) return
    const handler = (event: KeyboardEvent) => event.key === 'Escape' && onClose()
    document.addEventListener('keydown', handler)
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', handler)
      document.body.style.overflow = ''
    }
  }, [open, onClose])

  if (!open) return null
  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-ink/40 p-4" role="dialog" aria-modal="true">
      <div className={clsx('card w-full animate-fade-in shadow-pop', width)}>
        <header className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
          <div>
            <h2 className="text-[15px] leading-6">{title}</h2>
            {description && <p className="text-[13px] text-ink-soft">{description}</p>}
          </div>
          <button className="btn-ghost p-1.5" onClick={onClose} aria-label="Close">
            <X size={18} />
          </button>
        </header>
        <div className="max-h-[70vh] overflow-y-auto p-5">{children}</div>
      </div>
    </div>
  )
}

export function Field({ label, hint, error, children }: { label: string; hint?: string; error?: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="label">{label}</span>
      {children}
      {error ? <span className="mt-1 block text-xs text-critical-600">{error}</span>
        : hint && <span className="mt-1 block text-xs text-ink-faint">{hint}</span>}
    </label>
  )
}

export function Toggle({ checked, onChange, label }: { checked: boolean; onChange: (value: boolean) => void; label: string }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      onClick={() => onChange(!checked)}
      className={clsx(
        'relative h-5 w-9 shrink-0 rounded-full transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40',
        checked ? 'bg-brand-500' : 'bg-line-strong',
      )}
    >
      <span className={clsx('absolute top-0.5 h-4 w-4 rounded-full bg-white shadow-sm transition-all', checked ? 'left-[18px]' : 'left-0.5')} />
    </button>
  )
}
