import clsx from 'clsx'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { humidity as humidityText, since, temperature as temperatureText } from '@/lib/format'
import type { MapMarker } from '@/lib/types'

/**
 * Lightweight positional map. It plots live vehicles on a Nigeria bounding box without
 * requiring a Maps JavaScript key, and links out to Google Maps for the real thing.
 */
const BOUNDS = { minLat: 3.6, maxLat: 14.2, minLng: 2.5, maxLng: 15.0 }

// Rough corridor anchors so operators can orient themselves at a glance.
const CITIES = [
  { name: 'Lagos', lat: 6.5244, lng: 3.3792 },
  { name: 'Ibadan', lat: 7.3775, lng: 3.947 },
  { name: 'Abuja', lat: 9.0765, lng: 7.3986 },
  { name: 'Jos', lat: 9.8965, lng: 8.8583 },
  { name: 'Kano', lat: 12.0022, lng: 8.592 },
  { name: 'Port Harcourt', lat: 4.8156, lng: 7.0498 },
  { name: 'Maiduguri', lat: 11.8333, lng: 13.15 },
  { name: 'Benin City', lat: 6.335, lng: 5.6037 },
]

const project = (lat: number, lng: number) => ({
  x: ((lng - BOUNDS.minLng) / (BOUNDS.maxLng - BOUNDS.minLng)) * 100,
  y: (1 - (lat - BOUNDS.minLat) / (BOUNDS.maxLat - BOUNDS.minLat)) * 100,
})

const markerColour: Record<string, string> = {
  Normal: 'bg-brand-500',
  Warning: 'bg-warning-500',
  Critical: 'bg-critical-500',
  Offline: 'bg-ink-faint',
  Idle: 'bg-ink-faint',
}

export function FleetMap({ markers, height = 380 }: { markers: MapMarker[]; height?: number }) {
  const [active, setActive] = useState<MapMarker | null>(null)
  const plotted = useMemo(
    () => markers.filter((marker) => Number.isFinite(marker.latitude) && Number.isFinite(marker.longitude)),
    [markers],
  )

  return (
    <div className="relative bg-[linear-gradient(180deg,#f2f7f4,#eaf2ee)]" style={{ height }}>
      <svg className="absolute inset-0 h-full w-full" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden>
        <defs>
          <pattern id="wf-grid" width="8" height="8" patternUnits="userSpaceOnUse">
            <path d="M8 0H0v8" fill="none" stroke="#d7e4dc" strokeWidth="0.25" />
          </pattern>
        </defs>
        <rect width="100" height="100" fill="url(#wf-grid)" />
        {/* Indicative Lagos–Ibadan–Abuja–Kano corridor */}
        <polyline
          points={[CITIES[0], CITIES[1], CITIES[2], CITIES[4]]
            .map((city) => {
              const point = project(city.lat, city.lng)
              return `${point.x},${point.y}`
            })
            .join(' ')}
          fill="none"
          stroke="#b0e9c9"
          strokeWidth="0.8"
          strokeDasharray="2 1.5"
        />
      </svg>

      {CITIES.map((city) => {
        const point = project(city.lat, city.lng)
        return (
          <span
            key={city.name}
            className="pointer-events-none absolute -translate-x-1/2 -translate-y-1/2 text-[10px] font-medium uppercase tracking-wide text-ink-faint"
            style={{ left: `${point.x}%`, top: `${point.y}%` }}
          >
            <span className="mr-1 inline-block h-1 w-1 rounded-full bg-ink-faint align-middle" />
            {city.name}
          </span>
        )
      })}

      {plotted.map((marker) => {
        const point = project(marker.latitude, marker.longitude)
        return (
          <button
            key={marker.tripId}
            className="absolute -translate-x-1/2 -translate-y-1/2 focus:outline-none"
            style={{ left: `${point.x}%`, top: `${point.y}%` }}
            onClick={() => setActive(active?.tripId === marker.tripId ? null : marker)}
            aria-label={`${marker.fleetNumber}, ${marker.sensorStatus}`}
          >
            <span className="relative flex h-3.5 w-3.5">
              {marker.sensorStatus === 'Critical' && (
                <span className="absolute inline-flex h-full w-full animate-ring rounded-full bg-critical-500/60" />
              )}
              <span
                className={clsx(
                  'relative inline-flex h-3.5 w-3.5 rounded-full ring-2 ring-white',
                  markerColour[marker.sensorStatus] ?? 'bg-ink-faint',
                )}
              />
            </span>
          </button>
        )
      })}

      {active && (
        <div
          className="absolute z-10 w-64 -translate-x-1/2 rounded-xl border border-line bg-surface p-3 shadow-pop"
          style={{
            left: `${Math.min(Math.max(project(active.latitude, active.longitude).x, 18), 82)}%`,
            top: `${Math.min(project(active.latitude, active.longitude).y + 4, 74)}%`,
          }}
        >
          <div className="flex items-start justify-between gap-2">
            <div>
              <p className="text-sm font-semibold">{active.vehicleCode}</p>
              <p className="text-xs text-ink-soft">{active.route}</p>
            </div>
            <span className={clsx('mt-1 h-2.5 w-2.5 rounded-full', markerColour[active.sensorStatus])} />
          </div>
          <dl className="tabular mt-2.5 grid grid-cols-2 gap-x-3 gap-y-1.5 text-xs">
            <dt className="text-ink-faint">Driver</dt>
            <dd className="text-right font-medium">{active.driverName ?? '—'}</dd>
            <dt className="text-ink-faint">Temperature</dt>
            <dd className="text-right font-medium">{temperatureText(active.temperature)}</dd>
            <dt className="text-ink-faint">Humidity</dt>
            <dd className="text-right font-medium">{humidityText(active.humidity)}</dd>
            <dt className="text-ink-faint">Speed</dt>
            <dd className="text-right font-medium">{active.speedKmh == null ? '—' : `${active.speedKmh.toFixed(0)} km/h`}</dd>
            <dt className="text-ink-faint">Last fix</dt>
            <dd className="text-right font-medium">{since(active.lastPositionAt)}</dd>
          </dl>
          <div className="mt-3 flex gap-2">
            <Link to={`/trips/${active.tripId}`} className="btn-secondary flex-1 px-2 py-1.5 text-xs">
              Shipment
            </Link>
            <a
              className="btn-secondary flex-1 px-2 py-1.5 text-xs"
              target="_blank"
              rel="noreferrer"
              href={`https://www.google.com/maps?q=${active.latitude},${active.longitude}`}
            >
              Open in Maps
            </a>
          </div>
        </div>
      )}

      {plotted.length === 0 && (
        <p className="absolute inset-0 grid place-items-center text-sm text-ink-faint">
          No live positions yet — devices report as soon as a trip starts.
        </p>
      )}

      <div className="absolute bottom-3 left-3 flex flex-wrap gap-3 rounded-xl border border-line bg-surface/90 px-3 py-2 text-[11px] font-medium text-ink-soft backdrop-blur">
        {(['Normal', 'Warning', 'Critical', 'Offline'] as const).map((status) => (
          <span key={status} className="flex items-center gap-1.5">
            <span className={clsx('h-2 w-2 rounded-full', markerColour[status])} />
            {status}
          </span>
        ))}
      </div>
    </div>
  )
}
