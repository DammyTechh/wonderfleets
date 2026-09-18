import clsx from 'clsx'
import { AlertTriangle, ArrowDownRight, ArrowUpRight, ChevronLeft, ChevronRight, Loader2, Search, X } from 'lucide-react'
import type { ReactNode } from 'react'
import { useEffect } from 'react'
import type { SensorStatus, Severity } from '@/lib/types'

export function Card({ className, children }: { className?: string; children: ReactNode }) {
  return <section className={clsx('card', className)}>{children}</section>
}

export function CardHeader({ title, subtitle, action }: { title: string; subtitle?: string; action?: ReactNode }) {
  return (
    <header className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
      <div>
        <h2 className="text-base font-semibold">{title}</h2>
        {subtitle && <p className="mt-0.5 text-sm text-ink-soft">{subtitle}</p>}
      </div>
      {action}
    </header>
  )
}

export function PageHeader({ title, subtitle, action }: { title: string; subtitle?: string; action?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 className="text-2xl font-bold">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-ink-soft">{subtitle}</p>}
      </div>
      {action}
    </div>
  )
}

export function StatCard({
  icon, label, value, unit, delta, hint, tone = 'default',
}: {
  icon: ReactNode
  label: string
  value: string
  unit?: string
  delta?: { value: string; good?: boolean } | null
  hint?: string
  tone?: 'default' | 'critical' | 'warning' | 'brand'
}) {
  const tones = {
    default: 'bg-surface-sunken text-ink-soft',
    brand: 'bg-brand-100 text-brand-700',
    critical: 'bg-critical-50 text-critical-700',
    warning: 'bg-warning-50 text-warning-700',
  } as const
  return (
    <Card className="p-5">
      <div className="flex items-center justify-between">
        <span className={clsx('grid h-10 w-10 place-items-center rounded-xl', tones[tone])}>{icon}</span>
        {delta && (
          <span
            className={clsx(
              'chip',
              delta.good === false
                ? 'border-critical-100 bg-critical-50 text-critical-700'
                : 'border-brand-100 bg-brand-50 text-brand-700',
            )}
          >
            {delta.good === false ? <ArrowDownRight size={13} /> : <ArrowUpRight size={13} />}
            {delta.value}
          </span>
        )}
      </div>
      <p className="mt-4 text-sm font-medium text-ink-soft">{label}</p>
      <p className="tabular mt-1 text-3xl font-bold leading-none text-ink">
        {value}
        {unit && <span className="ml-1 text-base font-semibold text-ink-soft">{unit}</span>}
      </p>
      {hint && <p className="mt-2 text-xs text-ink-faint">{hint}</p>}
    </Card>
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
  const label = status.replace(/([a-z])([A-Z])/g, '$1 $2')
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
      {label}
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
    <div className="flex items-center justify-between border-t border-line px-5 py-3 text-sm text-ink-soft">
      <span className="tabular">
        Page {page} of {Math.max(totalPages, 1)} · {totalCount} record{totalCount === 1 ? '' : 's'}
      </span>
      <div className="flex gap-2">
        <button className="btn-secondary px-3 py-1.5" disabled={page <= 1} onClick={() => onChange(page - 1)}>
          <ChevronLeft size={16} /> Previous
        </button>
        <button className="btn-secondary px-3 py-1.5" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
          Next <ChevronRight size={16} />
        </button>
      </div>
    </div>
  )
}

export function EmptyState({ icon, title, description, action }: { icon?: ReactNode; title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 px-6 py-16 text-center">
      {icon && <span className="grid h-12 w-12 place-items-center rounded-2xl bg-surface-sunken text-ink-faint">{icon}</span>}
      <div>
        <p className="font-semibold text-ink">{title}</p>
        {description && <p className="mt-1 text-sm text-ink-soft">{description}</p>}
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
    <div className="m-5 flex items-start gap-3 rounded-xl border border-critical-100 bg-critical-50 p-4 text-sm text-critical-700">
      <AlertTriangle size={18} className="mt-0.5 shrink-0" />
      <div className="flex-1">
        <p className="font-medium">{message}</p>
        {onRetry && (
          <button className="mt-2 font-semibold underline underline-offset-2" onClick={onRetry}>
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
    <div className="fixed inset-0 z-50 grid place-items-center bg-ink/30 p-4 backdrop-blur-sm" role="dialog" aria-modal="true">
      <div className={clsx('card w-full animate-fade-in shadow-pop', width)}>
        <header className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
          <div>
            <h2 className="text-base font-semibold">{title}</h2>
            {description && <p className="mt-0.5 text-sm text-ink-soft">{description}</p>}
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
        'relative h-6 w-11 shrink-0 rounded-full transition focus:outline-none focus:ring-4 focus:ring-brand-500/15',
        checked ? 'bg-brand-600' : 'bg-line-strong',
      )}
    >
      <span className={clsx('absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-all', checked ? 'left-[22px]' : 'left-0.5')} />
    </button>
  )
}
