import { useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { BellRing, CheckCheck, ShieldCheck } from '@/components/icons'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { StatCard, StatStrip, Card, CardHeader, EmptyState, ErrorNote, Loading, PageHeader, Pagination, StatusChip, Table, Toggle } from '@/components/ui'
import { api, errorMessage } from '@/lib/api'
import { since } from '@/lib/format'
import { keys, useAlertActivity, useAlertRules, useAlertSummary, useAlerts, useChannels } from '@/lib/queries'

const STATES = [
  { key: 'open', label: 'Active alerts' },
  { key: 'resolved', label: 'Resolved' },
  { key: 'all', label: 'All' },
] as const

const channelName: Record<string, string> = { Email: 'Email', Sms: 'SMS', InApp: 'In-app' }

export default function Alerts() {
  const queryClient = useQueryClient()
  const [state, setState] = useState<'open' | 'resolved' | 'all'>('open')
  const [page, setPage] = useState(1)
  const [actionError, setActionError] = useState<string | null>(null)

  const summary = useAlertSummary()
  const alerts = useAlerts({ state, page, pageSize: 10 })
  const rules = useAlertRules()
  const channels = useChannels()
  const activity = useAlertActivity(7)

  async function act(request: Promise<unknown>) {
    setActionError(null)
    try {
      await request
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['alerts'] }),
        queryClient.invalidateQueries({ queryKey: keys.dashboard }),
      ])
    } catch (caught) {
      setActionError(errorMessage(caught))
    }
  }

  const levelColour: Record<string, string> = {
    None: 'bg-surface-sunken',
    Informational: 'bg-info-100',
    Warning: 'bg-warning-100',
    Critical: 'bg-critical-500',
  }

  return (
    <>
      <PageHeader title="Alerts" subtitle="Heat, humidity, route and device conditions that need attention." />

      <StatStrip columns={4}>
        <StatCard inStrip label="Critical" value={String(summary.data?.critical ?? 0)}
          tone={(summary.data?.critical ?? 0) > 0 ? 'critical' : 'default'} hint="need action now" />
        <StatCard inStrip label="Warning" value={String(summary.data?.warning ?? 0)}
          tone={(summary.data?.warning ?? 0) > 0 ? 'warning' : 'default'} hint="need attention" />
        <StatCard inStrip label="Informational" value={String(summary.data?.informational ?? 0)} hint="for your information" />
        <StatCard inStrip label="Resolved" value={String(summary.data?.resolved ?? 0)}
          hint={`in the last ${summary.data?.resolvedWindowDays ?? 30} days`} />
      </StatStrip>

      {actionError && <ErrorNote message={actionError} />}

      <div className="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-[1.7fr_1fr]">
        <Card className="self-start overflow-hidden">
          <div className="flex gap-1 border-b border-line p-3">
            {STATES.map((option) => (
              <button
                key={option.key}
                onClick={() => {
                  setState(option.key)
                  setPage(1)
                }}
                className={
                  option.key === state
                    ? 'rounded-lg bg-brand-50 px-3 py-1.5 text-sm font-semibold text-brand-800'
                    : 'rounded-lg px-3 py-1.5 text-sm font-medium text-ink-soft hover:bg-surface-muted'
                }
              >
                {option.label}
              </button>
            ))}
          </div>

          {alerts.isLoading ? (
            <Loading rows={4} />
          ) : alerts.error ? (
            <ErrorNote message={errorMessage(alerts.error)} onRetry={() => alerts.refetch()} />
          ) : alerts.data && alerts.data.items.length > 0 ? (
            <>
              <Table head={['Alert', 'Shipment', 'Reading', 'Raised', 'Action']}>
                {alerts.data.items.map((alert) => (
                  <tr key={alert.id} className="transition hover:bg-surface-muted">
                    <td className="td">
                      <span className="flex items-center gap-2">
                        <StatusChip status={alert.severity} pulse />
                      </span>
                      <span className="mt-1.5 block text-sm font-medium">{alert.title}</span>
                      <span className="block text-xs text-ink-faint">
                        {alert.deviceSerial ?? '—'}
                        {alert.occurrenceCount > 1 && ` · ${alert.occurrenceCount} occurrences`}
                      </span>
                    </td>
                    <td className="td">
                      {alert.tripId ? (
                        <Link to={`/trips/${alert.tripId}`} className="hover:text-brand-700 hover:underline">
                          <span className="block text-sm font-medium">{alert.fleetNumber ?? alert.tripCode}</span>
                          <span className="block text-xs text-ink-faint">{alert.route}</span>
                        </Link>
                      ) : (
                        <span className="text-ink-faint">—</span>
                      )}
                    </td>
                    <td className="td tabular whitespace-nowrap">
                      <span className="block font-medium">{alert.readingDisplay}</span>
                      {alert.thresholdDisplay && <span className="block text-xs text-ink-faint">{alert.thresholdDisplay}</span>}
                    </td>
                    <td className="td whitespace-nowrap text-xs text-ink-soft">{since(alert.triggeredAt)}</td>
                    <td className="td">
                      {alert.status === 'Resolved' ? (
                        <StatusChip status="Resolved" />
                      ) : (
                        <div className="flex gap-1.5">
                          {alert.status === 'Active' && (
                            <button className="btn-secondary px-2.5 py-1.5 text-xs" onClick={() => act(api.post(`/alerts/${alert.id}/acknowledge`))}>
                              Review
                            </button>
                          )}
                          <button className="btn-secondary px-2.5 py-1.5 text-xs" onClick={() => act(api.post(`/alerts/${alert.id}/resolve`, {}))}>
                            <CheckCheck size={14} /> Resolve
                          </button>
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </Table>
              <Pagination page={alerts.data.page} totalPages={alerts.data.totalPages} totalCount={alerts.data.totalCount} onChange={setPage} />
            </>
          ) : (
            <EmptyState icon={<ShieldCheck size={20} />} title="No alerts here" description="Cargo conditions are within their limits." />
          )}
        </Card>

        <div className="space-y-4">
          <Card>
            <CardHeader title="Alert rules" subtitle="Thresholds that trigger a notification" />
            <ul className="divide-y divide-line">
              {rules.data?.map((rule) => (
                <li key={rule.id} className="flex items-center gap-3 px-5 py-3">
                  <span className="min-w-0 flex-1">
                    <span className="block text-sm font-medium">{rule.name}</span>
                    <span className="block text-xs text-ink-faint">{rule.display}</span>
                  </span>
                  <Toggle
                    label={rule.name}
                    checked={rule.isEnabled}
                    onChange={(checked) =>
                      act(
                        api.put(`/alerts/rules/${rule.id}`, {
                          isEnabled: checked,
                          thresholdValue: rule.thresholdValue,
                          durationMinutes: rule.durationMinutes,
                        }),
                      )
                    }
                  />
                </li>
              ))}
            </ul>
          </Card>

          <Card>
            <CardHeader title="Notification channels" action={<BellRing size={18} className="text-ink-faint" />} />
            <ul className="divide-y divide-line">
              {channels.data?.map((channel) => (
                <li key={channel.id} className="px-5 py-3">
                  <div className="flex items-center gap-3">
                    <span className="min-w-0 flex-1">
                      <span className="block text-sm font-medium">{channelName[channel.channel] ?? channel.channel}</span>
                      <span className="block text-xs text-ink-faint">{channel.display}</span>
                    </span>
                    <Toggle
                      label={`${channelName[channel.channel] ?? channel.channel} enabled`}
                      checked={channel.isEnabled}
                      onChange={(checked) => act(api.put(`/alerts/channels/${channel.id}`, { isEnabled: checked, criticalOnly: channel.criticalOnly }))}
                    />
                  </div>
                  {channel.isEnabled && (
                    <label className="mt-2.5 flex items-center gap-2 text-xs text-ink-soft">
                      <input
                        type="checkbox"
                        className="h-3.5 w-3.5 rounded border-line-strong text-brand-600 focus:ring-brand-500/20"
                        checked={channel.criticalOnly}
                        onChange={(event) => act(api.put(`/alerts/channels/${channel.id}`, { isEnabled: channel.isEnabled, criticalOnly: event.target.checked }))}
                      />
                      Critical alerts only
                    </label>
                  )}
                </li>
              ))}
            </ul>
          </Card>

          <Card>
            <CardHeader title="Alerts activity" subtitle="Last 7 days by category" />
            <div className="overflow-x-auto p-5">
              <table className="w-full table-fixed">
                <colgroup><col className="w-24" />{activity.data?.days.map((d) => <col key={d} />)}</colgroup>
                <thead>
                  <tr>
                    <th />
                    {activity.data?.days.map((dayLabel) => (
                      <th key={dayLabel} className="pb-2 text-xs font-normal text-ink-soft">
                        {dayLabel}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {activity.data?.categories.map((category) => (
                    <tr key={category}>
                      <td className="pr-3 text-xs font-medium text-ink-soft">{category}</td>
                      {activity.data?.days.map((dayLabel) => {
                        const cell = activity.data?.cells.find((item) => item.day === dayLabel && item.category === category)
                        return (
                          <td key={dayLabel} className="p-1">
                            <span
                              title={`${cell?.count ?? 0} alerts`}
                              className={clsx('block h-6 w-full rounded-sm', levelColour[cell?.level ?? 'None'])}
                            />
                          </td>
                        )
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
        </div>
      </div>
    </>
  )
}
