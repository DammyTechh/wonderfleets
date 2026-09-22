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

const LATITUDES = [6, 8, 10, 12]
const LONGITUDES = [4, 6, 8, 10, 12, 14]

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
    <div className="relative overflow-hidden bg-[#EEF3F6]" style={{ height }}>
      {/* A real graticule every 2°, so positions read against actual latitude and longitude. */}
      <svg className="absolute inset-0 h-full w-full" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden>
        {LATITUDES.map((lat) => {
          const y = project(lat, BOUNDS.minLng).y
          return <line key={`lat${lat}`} x1="0" x2="100" y1={y} y2={y} stroke="#D9E2E9" strokeWidth="0.3" vectorEffect="non-scaling-stroke" />
        })}
        {LONGITUDES.map((lng) => {
          const x = project(BOUNDS.minLat, lng).x
          return <line key={`lng${lng}`} y1="0" y2="100" x1={x} x2={x} stroke="#D9E2E9" strokeWidth="0.3" vectorEffect="non-scaling-stroke" />
        })}
      </svg>
      {LATITUDES.map((lat) => (
        <span key={`latl${lat}`} className="pointer-events-none absolute right-2 hidden -translate-y-1/2 text-[10px] text-ink-faint sm:block"
          style={{ top: `${project(lat, BOUNDS.minLng).y}%` }}>{lat}°N</span>
      ))}
      {LONGITUDES.map((lng) => (
        <span key={`lngl${lng}`} className="pointer-events-none absolute top-1.5 -translate-x-1/2 text-[10px] text-ink-faint"
          style={{ left: `${project(BOUNDS.minLat, lng).x}%` }}>{lng}°E</span>
      ))}

      {CITIES.map((city) => {
        const point = project(city.lat, city.lng)
        return (
          <span
            key={city.name}
            className="pointer-events-none absolute -translate-x-1/2 -translate-y-1/2 whitespace-nowrap text-[11px] text-ink-soft"
            style={{ left: `${point.x}%`, top: `${point.y}%` }}
          >
            <span className="mr-1 inline-block h-1 w-1 rounded-full bg-ink-soft align-middle" />
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
          className="absolute z-10 w-64 -translate-x-1/2 rounded-lg border border-line bg-surface p-3 shadow-pop"
          style={{
            left: `${Math.min(Math.max(project(active.latitude, active.longitude).x, 18), 82)}%`,
            top: `${Math.min(project(active.latitude, active.longitude).y + 4, 74)}%`,
          }}
        >
          <div className="flex items-start justify-between gap-2">
            <div>
              <p className="text-sm font-medium">{active.fleetNumber} <span className="font-normal text-ink-soft">{active.vehicleCode}</span></p>
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
            <Link to={`/trips/${active.tripId}`} className="btn-secondary h-8 flex-1 px-2 text-xs">
              View shipment
            </Link>
            <a
              className="btn-secondary h-8 flex-1 px-2 text-xs"
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
          No positions yet. Trucks appear here once a trip starts.
        </p>
      )}

      <div className="absolute bottom-3 right-3 flex flex-wrap gap-3 rounded-md border border-line bg-surface px-2.5 py-1.5 text-[11px] text-ink-soft">
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
