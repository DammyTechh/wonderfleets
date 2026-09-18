import { Download, Link2, Plus, Sprout } from 'lucide-react'
import { useState } from 'react'
import {
  Avatar, Card, EmptyState, ErrorNote, Loading, PageHeader, Pagination, SearchInput, StatusChip, Table,
} from '@/components/ui'
import { download, errorMessage } from '@/lib/api'
import { useProcessors } from '@/lib/queries'
import type { ProcessorListItem } from '@/lib/types'
import { CreateProcessorDialog } from '@/components/CreateProcessorDialog'
import { ShareLinkDialog } from './Partners'

export default function Processors() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [sharing, setSharing] = useState<ProcessorListItem | null>(null)
  const [creating, setCreating] = useState(false)

  const { data, isLoading, error, refetch } = useProcessors({ page, pageSize: 10, search: search || undefined })

  return (
    <>
      <PageHeader
        title="Agro-processors"
        subtitle="Produce owners, their facilities and their shipment history."
        action={
          <div className="flex gap-2">
            <button className="btn-secondary" onClick={() => download('/agro-processors/export', { search: search || undefined })}>
              <Download size={16} /> Export
            </button>
            <button className="btn-accent" onClick={() => setCreating(true)}>
              <Plus size={16} /> Add processor
            </button>
          </div>
        }
      />

      <Card>
        <div className="border-b border-line p-4">
          <div className="max-w-md">
            <SearchInput value={search} onChange={(value) => { setSearch(value); setPage(1) }} placeholder="Search processor or contact" />
          </div>
        </div>

        {isLoading ? (
          <Loading rows={5} />
        ) : error ? (
          <ErrorNote message={errorMessage(error)} onRetry={() => refetch()} />
        ) : data && data.items.length > 0 ? (
          <>
            <Table head={['Processor', 'Contact person', 'Shipments', 'Location', 'Status', '']}>
              {data.items.map((processor) => (
                <tr key={processor.id} className="transition hover:bg-surface-muted">
                  <td className="td">
                    <span className="flex items-center gap-3">
                      <Avatar initials={processor.initials} photoUrl={processor.photoUrl} />
                      <span>
                        <span className="block font-semibold">{processor.name}</span>
                        <span className="block font-mono text-xs text-ink-faint">{processor.processorCode}</span>
                      </span>
                    </span>
                  </td>
                  <td className="td">
                    <span className="block">{processor.contactName ?? '—'}</span>
                    <span className="block text-xs text-ink-faint">{processor.contactPhone ?? processor.contactEmail ?? ''}</span>
                  </td>
                  <td className="td tabular">{processor.fleetOrders}</td>
                  <td className="td">{processor.location}</td>
                  <td className="td"><StatusChip status={processor.status} /></td>
                  <td className="td text-right">
                    <button className="btn-secondary px-3 py-1.5 text-xs" onClick={() => setSharing(processor)}>
                      <Link2 size={14} /> Manage access
                    </button>
                  </td>
                </tr>
              ))}
            </Table>
            <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={setPage} />
          </>
        ) : (
          <EmptyState
            icon={<Sprout size={20} />}
            title="No agro-processors yet"
            description="Add the produce owners you move goods for — a shipment cannot be booked without one."
            action={<button className="btn-accent" onClick={() => setCreating(true)}><Plus size={16} /> Add processor</button>}
          />
        )}
      </Card>

      <CreateProcessorDialog open={creating} onClose={() => setCreating(false)} />

      <ShareLinkDialog
        open={Boolean(sharing)}
        audience="AgroProcessor"
        partnerId={sharing?.id}
        partnerName={sharing?.name}
        defaultEmail={sharing?.contactEmail}
        onClose={() => setSharing(null)}
      />
    </>
  )
}
