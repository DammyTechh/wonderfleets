import { useQueryClient } from '@tanstack/react-query'
import { BellOff, CheckCheck, Cpu, TriangleAlert, Users } from '@/components/icons'
import { useState } from 'react'
import { Avatar, Card, EmptyState, ErrorNote, Loading, PageHeader, Pagination } from '@/components/ui'
import { api, errorMessage } from '@/lib/api'
import { since } from '@/lib/format'
import { useNotifications } from '@/lib/queries'
import type { NotificationItem } from '@/lib/types'

const categoryIcon = {
  Hardware: Cpu,
  Critical: TriangleAlert,
  Partner: Users,
  System: CheckCheck,
} as const

const categoryStyle: Record<string, string> = {
  Hardware: 'border-info-100 bg-info-50 text-info-700',
  Critical: 'border-critical-100 bg-critical-50 text-critical-700',
  Partner: 'border-brand-100 bg-brand-50 text-brand-700',
  System: 'border-line bg-surface-muted text-ink-soft',
}

export default function Notifications() {
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const { data, isLoading, error: loadError, refetch } = useNotifications({ page, pageSize: 20, unreadOnly })

  async function act(request: Promise<unknown>) {
    setError(null)
    try {
      await request
      await queryClient.invalidateQueries({ queryKey: ['notifications'] })
    } catch (caught) {
      setError(errorMessage(caught))
    }
  }

  const groups = (data?.items ?? []).reduce<Record<string, NotificationItem[]>>((accumulator, item) => {
    ;(accumulator[item.group] ??= []).push(item)
    return accumulator
  }, {})

  return (
    <>
      <PageHeader
        title="Notifications"
        subtitle="Hardware, partner and system events, newest first."
        action={
          <div className="flex gap-2">
            <button
              className={unreadOnly ? 'btn-primary' : 'btn-secondary'}
              onClick={() => {
                setUnreadOnly((value) => !value)
                setPage(1)
              }}
            >
              Unread only
            </button>
            <button className="btn-secondary" onClick={() => act(api.post('/notifications/read-all'))}>
              <CheckCheck size={16} /> Mark all as read
            </button>
          </div>
        }
      />

      {error && <ErrorNote message={error} />}

      <Card className="overflow-hidden">
        {isLoading ? (
          <Loading rows={5} />
        ) : loadError ? (
          <ErrorNote message={errorMessage(loadError)} onRetry={() => refetch()} />
        ) : data && data.items.length > 0 ? (
          <>
            {Object.entries(groups).map(([group, items]) => (
              <section key={group}>
                <h2 className="border-b border-line bg-surface-muted px-5 py-2 text-xs font-semibold text-ink-faint">
                  {group}
                </h2>
                <ul className="divide-y divide-line">
                  {items.map((item) => {
                    const Icon = categoryIcon[item.category] ?? CheckCheck
                    return (
                      <li key={item.id} className={item.isRead ? 'px-5 py-4' : 'bg-brand-50/40 px-5 py-4'}>
                        <div className="flex items-start gap-3">
                          {item.partnerInitials ? (
                            <Avatar initials={item.partnerInitials} size={38} />
                          ) : (
                            <span className="grid h-9 w-9 shrink-0 place-items-center rounded-md bg-surface-sunken text-ink-soft">
                              <Icon size={17} />
                            </span>
                          )}
                          <div className="min-w-0 flex-1">
                            <div className="flex flex-wrap items-center gap-2">
                              <span className={`chip ${categoryStyle[item.category]}`}>{item.category}</span>
                              <p className="text-sm font-semibold">{item.title}</p>
                              <span className="ml-auto text-xs text-ink-faint">{since(item.createdAt)}</span>
                            </div>
                            <p className="mt-1 text-sm text-ink-soft">{item.message}</p>
                            <div className="mt-2.5 flex gap-2">
                              {item.requiresReview && !item.isRead && (
                                <button className="btn-secondary px-2.5 py-1.5 text-xs" onClick={() => act(api.post(`/notifications/${item.id}/read`))}>
                                  Review
                                </button>
                              )}
                              <button className="btn-ghost px-2.5 py-1.5 text-xs" onClick={() => act(api.post(`/notifications/${item.id}/dismiss`))}>
                                Dismiss
                              </button>
                            </div>
                          </div>
                        </div>
                      </li>
                    )
                  })}
                </ul>
              </section>
            ))}
            <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={setPage} />
          </>
        ) : (
          <EmptyState icon={<BellOff size={20} />} title="Nothing to read" description="New hardware and partner events will appear here." />
        )}
      </Card>
    </>
  )
}
