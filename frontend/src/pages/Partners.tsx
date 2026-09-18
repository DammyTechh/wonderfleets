import { Copy, Download, Link2, Plus, Users } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { CreatePartnerDialog } from '@/components/CreatePartnerDialog'
import {
  Avatar, Card, EmptyState, ErrorNote, Field, Loading, Modal, PageHeader, Pagination, SearchInput, Spinner,
  StatusChip, Table,
} from '@/components/ui'
import { download, errorMessage } from '@/lib/api'
import { dt } from '@/lib/format'
import { createShareLink, usePartners } from '@/lib/queries'
import type { PartnerListItem, ShareLinkCreated } from '@/lib/types'

export default function Partners() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [sharing, setSharing] = useState<PartnerListItem | null>(null)
  const [creating, setCreating] = useState(false)

  const params = { page, pageSize: 10, search: search || undefined }
  const { data, isLoading, error, refetch } = usePartners(params)

  return (
    <>
      <PageHeader
        title="Logistics partners"
        subtitle="Transporters, their fleets and their compliance standing."
        action={
          <div className="flex gap-2">
            <button className="btn-secondary" onClick={() => download('/logistics-partners/export', { search: search || undefined })}>
              <Download size={16} /> Export
            </button>
            <button className="btn-accent" onClick={() => setCreating(true)}>
              <Plus size={16} /> Add partner
            </button>
          </div>
        }
      />

      <Card>
        <div className="border-b border-line p-4">
          <div className="max-w-md">
            <SearchInput
              value={search}
              onChange={(value) => {
                setSearch(value)
                setPage(1)
              }}
              placeholder="Search company, contact or corridor"
            />
          </div>
        </div>

        {isLoading ? (
          <Loading rows={5} />
        ) : error ? (
          <ErrorNote message={errorMessage(error)} onRetry={() => refetch()} />
        ) : data && data.items.length > 0 ? (
          <>
            <Table head={['Partner', 'Contact', 'Fleet size', 'Location', 'Status', '']}>
              {data.items.map((partner) => (
                <tr key={partner.id} className="transition hover:bg-surface-muted">
                  <td className="td">
                    <Link to={`/partners/${partner.id}`} className="flex items-center gap-3">
                      <Avatar initials={partner.initials} photoUrl={partner.photoUrl} />
                      <span>
                        <span className="block font-semibold">{partner.companyName}</span>
                        <span className="block font-mono text-xs text-ink-faint">{partner.partnerCode}</span>
                      </span>
                    </Link>
                  </td>
                  <td className="td">
                    <span className="block">{partner.contactPerson}</span>
                    <span className="block text-xs text-ink-faint">{partner.phoneNumber}</span>
                  </td>
                  <td className="td tabular">{partner.fleetSize}</td>
                  <td className="td">{partner.location}</td>
                  <td className="td">
                    <StatusChip status={partner.status} />
                  </td>
                  <td className="td text-right">
                    <button className="btn-secondary px-3 py-1.5 text-xs" onClick={() => setSharing(partner)}>
                      <Link2 size={14} /> Partner access
                    </button>
                  </td>
                </tr>
              ))}
            </Table>
            <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={setPage} />
          </>
        ) : (
          <EmptyState icon={<Users size={20} />} title="No partners yet" description="Onboard a transporter to start assigning shipments." />
        )}
      </Card>

      <CreatePartnerDialog open={creating} onClose={() => setCreating(false)} />

      <ShareLinkDialog
        open={Boolean(sharing)}
        audience="LogisticsPartner"
        partnerId={sharing?.id}
        partnerName={sharing?.companyName}
        defaultEmail={sharing?.email}
        onClose={() => setSharing(null)}
      />
    </>
  )
}

export function ShareLinkDialog({
  open, audience, partnerId, partnerName, defaultEmail, onClose,
}: {
  open: boolean
  audience: 'LogisticsPartner' | 'AgroProcessor'
  partnerId?: string
  partnerName?: string
  defaultEmail?: string | null
  onClose: () => void
}) {
  const [sendEmail, setSendEmail] = useState(true)
  const [email, setEmail] = useState('')
  const [busy, setBusy] = useState(false)
  const [created, setCreated] = useState<ShareLinkCreated | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  async function generate() {
    if (!partnerId) return
    setBusy(true)
    setError(null)
    try {
      const result = await createShareLink({
        audience,
        partnerId,
        sendEmail,
        recipientEmail: sendEmail ? (email.trim() || defaultEmail || null) : null,
      })
      setCreated(result)
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setBusy(false)
    }
  }

  function close() {
    setCreated(null)
    setError(null)
    setCopied(false)
    setEmail('')
    onClose()
  }

  return (
    <Modal
      open={open}
      title="Partner access"
      description="Generate a temporary link that expires after the trip. No login is required."
      onClose={close}
    >
      {created ? (
        <div className="space-y-4">
          <div className="rounded-xl border border-brand-100 bg-brand-50 p-4">
            <p className="text-sm font-medium text-brand-800">Tracking link ready</p>
            <p className="mt-1 text-xs text-brand-700">
              Covers {created.tripCount} shipment{created.tripCount === 1 ? '' : 's'} · expires {dt(created.expiresAt)}
              {created.emailQueued && ' · emailed to the contact'}
            </p>
          </div>
          <div className="flex gap-2">
            <input className="input font-mono text-xs" readOnly value={created.url} onFocus={(event) => event.target.select()} />
            <button
              className="btn-secondary shrink-0"
              onClick={async () => {
                await navigator.clipboard?.writeText(created.url)
                setCopied(true)
              }}
            >
              <Copy size={15} /> {copied ? 'Copied' : 'Copy'}
            </button>
          </div>
          <p className="text-xs text-ink-faint">
            Anyone with this link can see the shipment, so share it privately. It stops working automatically when the
            trip ends, and you can revoke it at any time.
          </p>
          <div className="flex justify-end">
            <button className="btn-primary" onClick={close}>
              Done
            </button>
          </div>
        </div>
      ) : (
        <div className="space-y-4">
          <p className="text-sm text-ink-soft">
            {partnerName} will receive{' '}
            {audience === 'LogisticsPartner'
              ? 'fleet management and live tracking, without cargo climate readings.'
              : 'live tracking of their produce, including temperature and humidity.'}
          </p>
          <label className="flex items-start gap-3 text-sm">
            <input
              type="checkbox"
              className="mt-0.5 h-4 w-4 rounded border-line-strong text-brand-600 focus:ring-brand-500/20"
              checked={sendEmail}
              onChange={(event) => setSendEmail(event.target.checked)}
            />
            <span>
              Email the link
              <span className="block text-xs text-ink-faint">Defaults to the registered contact address.</span>
            </span>
          </label>
          {sendEmail && (
            <Field label="Recipient email" hint={defaultEmail ? `Leave blank to use ${defaultEmail}` : undefined}>
              <input className="input" type="email" value={email} onChange={(event) => setEmail(event.target.value)} />
            </Field>
          )}
          {error && <ErrorNote message={error} />}
          <div className="flex justify-end gap-2">
            <button className="btn-secondary" onClick={close}>
              Cancel
            </button>
            <button className="btn-primary" disabled={busy} onClick={generate}>
              {busy ? <Spinner /> : <Link2 size={16} />} Generate tracking link
            </button>
          </div>
        </div>
      )}
    </Modal>
  )
}
