import { useQuery } from '@tanstack/react-query'
import { Truck } from 'lucide-react'
import { FleetMap } from '@/components/FleetMap'
import { Card, CardHeader, EmptyState, ErrorNote, Loading, StatusChip, Table } from '@/components/ui'
import { errorMessage, portalApi } from '@/lib/api'
import { since } from '@/lib/format'
import type { LogisticsTracking, Paged, PortalVehicle } from '@/lib/types'
import { PortalShell, usePortalSession } from './PortalShell'

/** Transporter view: vehicles, drivers and positions — deliberately without cargo climate readings. */
export default function LogisticsPortal() {
  const { session } = usePortalSession('LogisticsPartner')

  const vehicles = useQuery({
    queryKey: ['portal', 'logistics', 'vehicles'],
    queryFn: async () => (await portalApi.get<Paged<PortalVehicle>>('/portal/logistics/vehicles')).data,
    refetchInterval: 30_000,
    enabled: Boolean(session),
  })

  const tracking = useQuery({
    queryKey: ['portal', 'logistics', 'tracking'],
    queryFn: async () => (await portalApi.get<LogisticsTracking>('/portal/logistics/tracking')).data,
    refetchInterval: 20_000,
    enabled: Boolean(session),
  })

  return (
    <PortalShell session={session} subtitle="Fleet tracking">
      <div className="grid gap-4">
        <Card className="overflow-hidden">
          <CardHeader
            title="Live positions"
            subtitle={
              tracking.data
                ? `${tracking.data.gpsMonitor.activeSignals} active signals · ${tracking.data.gpsMonitor.localTime} ${tracking.data.gpsMonitor.timeZone}`
                : 'Waiting for the first fix'
            }
          />
          {tracking.isLoading ? <Loading rows={3} /> : <FleetMap markers={tracking.data?.markers ?? []} height={360} />}
          {(tracking.data?.actionAlerts.length ?? 0) > 0 && (
            <ul className="divide-y divide-line border-t border-line">
              {tracking.data!.actionAlerts.slice(0, 3).map((alert) => (
                <li key={`${alert.fleetNumber}-${alert.at}`} className="flex items-start gap-3 px-5 py-3">
                  <StatusChip status={alert.severity} pulse />
                  <span className="min-w-0 flex-1">
                    <span className="block text-sm font-medium">{alert.title}</span>
                    <span className="block text-xs text-ink-soft">
                      {alert.fleetNumber} · {alert.message}
                    </span>
                  </span>
                  <span className="whitespace-nowrap text-xs text-ink-faint">{since(alert.at)}</span>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card className="overflow-hidden">
          <CardHeader title="Your vehicles on this shipment" subtitle="Assignment, route and current status" />
          {vehicles.isLoading ? (
            <Loading rows={4} />
          ) : vehicles.error ? (
            <ErrorNote message={errorMessage(vehicles.error)} onRetry={() => vehicles.refetch()} />
          ) : vehicles.data && vehicles.data.items.length > 0 ? (
            <Table head={['Vehicle', 'Fleet no.', 'Driver', 'Route', 'Status']}>
              {vehicles.data.items.map((vehicle) => (
                <tr key={vehicle.tripId}>
                  <td className="td font-mono text-[13px]">{vehicle.vehicleCode}</td>
                  <td className="td font-medium">{vehicle.fleetNumber}</td>
                  <td className="td">{vehicle.driverName ?? 'Unassigned'}</td>
                  <td className="td">{vehicle.route}</td>
                  <td className="td">
                    <StatusChip status={vehicle.tripStatus} />
                  </td>
                </tr>
              ))}
            </Table>
          ) : (
            <EmptyState icon={<Truck size={20} />} title="No active shipment" description="This link covers shipments that have finished." />
          )}
        </Card>
      </div>
    </PortalShell>
  )
}
