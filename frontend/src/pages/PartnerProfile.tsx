import { ArrowLeft, BadgeCheck, FileText, Truck, Users } from '@/components/icons'
import { Link, useParams } from 'react-router-dom'
import { Avatar, Card, CardHeader, ErrorNote, Loading, PageHeader, StatusChip } from '@/components/ui'
import { errorMessage } from '@/lib/api'
import { day } from '@/lib/format'
import { usePartner } from '@/lib/queries'
import { label } from '@/lib/format'

export default function PartnerProfile() {
  const { partnerId = '' } = useParams()
  const { data, isLoading, error, refetch } = usePartner(partnerId)

  if (isLoading) return <Loading rows={6} />
  if (error || !data) return <ErrorNote message={errorMessage(error)} onRetry={() => refetch()} />

  const { stats, documents, driverPool, recentShipments, contact, company, corridors, fleetComposition } = data

  return (
    <>
      <PageHeader
        title={data.companyName}
        subtitle={`${data.partnerCode} · ${contact.location}`}
        action={
          <Link to="/partners" className="btn-secondary">
            <ArrowLeft size={16} /> All partners
          </Link>
        }
      />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1fr_2fr]">
        <Card className="p-5">
          <div className="flex items-center gap-3">
            <Avatar initials={data.initials} photoUrl={data.photoUrl} size={52} />
            <div>
              <p className="font-semibold">{contact.contactPerson}</p>
              <p className="text-sm text-ink-soft">{contact.email}</p>
              <p className="text-sm text-ink-soft">{contact.phoneNumber}</p>
            </div>
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <StatusChip status={data.status} />
            <span className="chip border-line bg-surface-muted text-ink-soft">
              {label(company.availabilityStatus)}
            </span>
            <span className="chip border-line bg-surface-muted text-ink-soft">CAC {company.cacNumber}</span>
          </div>
          <dl className="mt-5 grid grid-cols-3 gap-3 text-center">
            {[
              ['Fleet size', stats.fleetSize],
              ['Completed', stats.completedTrips],
              ['Active', stats.activeShipments],
            ].map(([label, value]) => (
              <div key={String(label)} className="rounded-md bg-surface-sunken p-3">
                <dt className="text-xs text-ink-faint">{String(label)}</dt>
                <dd className="tabular mt-1 text-lg font-semibold">{Number(value)}</dd>
              </div>
            ))}
          </dl>
          {corridors.length > 0 && (
            <div className="mt-5">
              <p className="label">Operating corridors</p>
              <div className="flex flex-wrap gap-1.5">
                {corridors.map((corridor) => (
                  <span key={corridor} className="chip border-line bg-surface-muted text-ink-soft">
                    {corridor}
                  </span>
                ))}
              </div>
            </div>
          )}

          {fleetComposition.length > 0 && (
            <div className="mt-5">
              <p className="label">Fleet composition</p>
              <ul className="space-y-1.5 text-sm">
                {fleetComposition.map((truck) => (
                  <li key={truck.id} className="flex items-center justify-between gap-3">
                    <span className="text-ink-soft">{truck.truckType}</span>
                    <span className="tabular font-medium">
                      {truck.quantity} × {truck.maxTonnage} t
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </Card>

        <div className="space-y-4">
          <Card>
            <CardHeader title="Driver pool" subtitle="Availability right now" />
            <div className="grid grid-cols-3 divide-x divide-line">
              {[
                ['On trip', driverPool.onTrip],
                ['Available', driverPool.available],
                ['Off duty', driverPool.offDuty],
              ].map(([label, value]) => (
                <div key={String(label)} className="px-5 py-4 text-center">
                  <p className="flex items-center justify-center gap-1.5 text-xs text-ink-faint">
                    <Users size={13} /> {String(label)}
                  </p>
                  <p className="tabular mt-1 text-2xl font-semibold">{Number(value)}</p>
                </div>
              ))}
            </div>
          </Card>

          <Card>
            <CardHeader title="Compliance & documentation" />
            <ul className="divide-y divide-line">
              {documents.map((document) => (
                <li key={document.id} className="flex items-center gap-3 px-5 py-3">
                  <FileText size={16} className="text-ink-faint" />
                  <span className="min-w-0 flex-1">
                    <span className="block text-sm font-medium">{label(document.documentType)}</span>
                    <span className="block truncate text-xs text-ink-faint">{document.fileName}</span>
                  </span>
                  {document.expiresOn && <span className="text-xs text-ink-soft">Expires {day(document.expiresOn)}</span>}
                  {document.isVerified ? (
                    <span className="chip border-brand-100 bg-brand-50 text-brand-700">
                      <BadgeCheck size={13} /> Verified
                    </span>
                  ) : (
                    <span className="chip border-warning-100 bg-warning-50 text-warning-700">Pending</span>
                  )}
                </li>
              ))}
              {documents.length === 0 && <li className="px-5 py-8 text-center text-sm text-ink-faint">No documents uploaded yet.</li>}
            </ul>
          </Card>

          <Card>
            <CardHeader title="Recent shipments" />
            <ul className="divide-y divide-line">
              {recentShipments.map((shipment) => (
                <li key={shipment.tripId}>
                  <Link to={`/trips/${shipment.tripId}`} className="flex items-center gap-3 px-5 py-3 hover:bg-surface-muted">
                    <Truck size={16} className="text-ink-faint" />
                    <span className="min-w-0 flex-1">
                      <span className="block text-sm font-medium">{shipment.tripCode} · {shipment.fleetNumber}</span>
                      <span className="block truncate text-xs text-ink-faint">{shipment.route}</span>
                    </span>
                    <StatusChip status={shipment.status} />
                  </Link>
                </li>
              ))}
              {recentShipments.length === 0 && <li className="px-5 py-8 text-center text-sm text-ink-faint">No shipments yet.</li>}
            </ul>
          </Card>
        </div>
      </div>
    </>
  )
}
