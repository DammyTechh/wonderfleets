import clsx from 'clsx'
import { Plus, Truck } from '@/components/icons'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import {
  Avatar, Card, EmptyState, ErrorNote, Loading, PageHeader, Pagination, SearchInput, StatusChip, Table,
} from '@/components/ui'
import { errorMessage } from '@/lib/api'
import { humidity as humidityText, temperature as temperatureText, tonnes } from '@/lib/format'
import { useFleet } from '@/lib/queries'
import type { SensorStatus } from '@/lib/types'

const FILTERS: (SensorStatus | 'All')[] = ['All', 'Normal', 'Warning', 'Critical', 'Offline']

export default function Fleet() {
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<SensorStatus | 'All'>('All')
  const [page, setPage] = useState(1)

  const params = {
    page,
    pageSize: 10,
    search: search || undefined,
    status: status === 'All' ? undefined : status,
  }
  const { data, isLoading, error, refetch } = useFleet(params)

  return (
    <>
      <PageHeader
        title="Fleet management"
        subtitle="Every vehicle, its current shipment and live cargo conditions."
        action={
          <Link to="/fleet/new" className="btn-accent">
            <Plus size={16} /> Add a fleet
          </Link>
        }
      />

      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-line p-4">
          <div className="min-w-[240px] flex-1">
            <SearchInput
              value={search}
              onChange={(value) => {
                setSearch(value)
                setPage(1)
              }}
              placeholder="Search vehicle, fleet number, partner or driver"
            />
          </div>
          <div className="flex flex-wrap gap-1.5">
            {FILTERS.map((option) => (
              <button
                key={option}
                onClick={() => {
                  setStatus(option)
                  setPage(1)
                }}
                className={
                  option === status
                    ? 'chip border-brand-200 bg-brand-50 text-brand-800'
                    : 'chip border-line bg-surface text-ink-soft hover:bg-surface-muted'
                }
              >
                {option}
              </button>
            ))}
          </div>
        </div>

        {isLoading ? (
          <Loading rows={5} />
        ) : error ? (
          <ErrorNote message={errorMessage(error)} onRetry={() => refetch()} />
        ) : data && data.items.length > 0 ? (
          <>
            <Table head={['Vehicle ID', 'Fleet no.', 'Partner', 'Driver', 'Route', 'Temp & hum.', 'Status']}>
              {data.items.map((row) => (
                <tr key={row.vehicleId} className="transition hover:bg-surface-muted">
                  <td className="td">
                    <p className="tabular text-[13px] font-medium">{row.vehicleCode}</p>
                    <p className="text-xs text-ink-faint">
                      {row.vehicleType} · {tonnes(row.capacityTonnes)}
                    </p>
                  </td>
                  <td className="td"><span className="code-id">{row.fleetNumber}</span></td>
                  <td className="td">
                    <Link to={`/partners/${row.partnerId}`} className="hover:text-brand-700 hover:underline">
                      {row.partnerName}
                    </Link>
                  </td>
                  <td className="td">
                    {row.driverName ? (
                      <span className="flex items-center gap-2">
                        <Avatar initials={row.driverInitials ?? '—'} size={28} />
                        <span className="text-sm">{row.driverName}</span>
                      </span>
                    ) : (
                      <span className="text-ink-faint">Unassigned</span>
                    )}
                  </td>
                  <td className="td">
                    {row.tripId ? (
                      <Link to={`/trips/${row.tripId}`} className="hover:text-brand-700 hover:underline">
                        {row.route}
                      </Link>
                    ) : (
                      <span className="text-ink-faint">No active shipment</span>
                    )}
                  </td>
                  <td className="td tabular whitespace-nowrap">
                    <span className={clsx('font-medium', row.status === 'Critical' && 'text-critical-600', row.status === 'Warning' && 'text-warning-600')}>
                      {temperatureText(row.temperature)}
                    </span>
                    <span className="ml-2 text-ink-soft">{humidityText(row.humidity)}</span>
                  </td>
                  <td className="td">
                    <StatusChip status={row.status} pulse />
                  </td>
                </tr>
              ))}
            </Table>
            <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={setPage} />
          </>
        ) : (
          <EmptyState
            icon={<Truck size={20} />}
            title="No vehicles match this view"
            description="Register a vehicle and schedule its first shipment with the Add a Fleet wizard."
            action={
              <Link to="/fleet/new" className="btn-accent">
                <Plus size={16} /> Add a fleet
              </Link>
            }
          />
        )}
      </Card>
    </>
  )
}
