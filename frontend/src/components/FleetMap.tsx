import clsx from 'clsx'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Crosshair, MapPin, Maximize2, Minus, Plus, Truck } from '@/components/icons'
import { Modal, Spinner } from '@/components/ui'
import { humidity as humidityText, since, temperature as temperatureText } from '@/lib/format'
import { useTripTrack } from '@/lib/queries'
import { GoogleFleetMap } from '@/components/GoogleFleetMap'
import { hasGoogleMapsKey } from '@/lib/googleMaps'
import type { MapMarker, StatusCounts, TrackPoint } from '@/lib/types'

/**
 * Positional map for live vehicles.
 *
 * It plots real coordinates on a latitude/longitude grid without a Maps JavaScript key,
 * and links out to Google Maps when someone wants roads and satellite imagery. Markers
 * come straight from the tracking feed, so they move as the hardware reports.
 *
 * Each vehicle is drawn as a truck with its fleet number beside it rather than an
 * anonymous dot, because an operator looking at six trucks needs to know which is which
 * without clicking each one. Colour carries the sensor state.
 *
 * Expanded, it zooms to the vehicles actually reporting and draws where the selected one
 * has been. One dot on a map of Nigeria cannot show movement; a breadcrumb can.
 */

type Bounds = { minLat: number; maxLat: number; minLng: number; maxLng: number }

const NIGERIA: Bounds = { minLat: 3.6, maxLat: 14.2, minLng: 2.5, maxLng: 15.0 }

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
  { name: 'Kaduna', lat: 10.5222, lng: 7.4383 },
  { name: 'Lokoja', lat: 7.8023, lng: 6.7333 },
  { name: 'Ilorin', lat: 8.4966, lng: 4.5421 },
  { name: 'Enugu', lat: 6.4584, lng: 7.5464 },
]

/** Sensor state drives the colour: the same vocabulary as the status chips elsewhere. */
const TONE = {
  Normal: { dot: 'bg-normal-500', ink: 'text-normal-600', edge: 'border-normal-500/50', fill: 'bg-normal-600' },
  Warning: { dot: 'bg-warning-500', ink: 'text-warning-600', edge: 'border-warning-500/50', fill: 'bg-warning-600' },
  Critical: { dot: 'bg-critical-500', ink: 'text-critical-600', edge: 'border-critical-500/50', fill: 'bg-critical-600' },
  Offline: { dot: 'bg-ink-faint', ink: 'text-ink-soft', edge: 'border-line-strong', fill: 'bg-ink-soft' },
  Idle: { dot: 'bg-ink-faint', ink: 'text-ink-soft', edge: 'border-line-strong', fill: 'bg-ink-soft' },
} as const

const tone = (status: string) => TONE[status as keyof typeof TONE] ?? TONE.Offline

const projector = (bounds: Bounds) => (lat: number, lng: number) => ({
  x: ((lng - bounds.minLng) / (bounds.maxLng - bounds.minLng)) * 100,
  y: (1 - (lat - bounds.minLat) / (bounds.maxLat - bounds.minLat)) * 100,
})

/** Frames the points with a margin, never zooming so far that a single fix fills the view. */
function fit(points: { latitude: number; longitude: number }[]): Bounds {
  if (points.length === 0) return NIGERIA
  const lats = points.map((p) => p.latitude)
  const lngs = points.map((p) => p.longitude)
  const minLat = Math.min(...lats)
  const maxLat = Math.max(...lats)
  const minLng = Math.min(...lngs)
  const maxLng = Math.max(...lngs)
  // A stationary vehicle has one point and no span at all, so impose a floor.
  const latSpan = Math.max(maxLat - minLat, 0.45)
  const lngSpan = Math.max(maxLng - minLng, 0.45)
  // Generous headroom on purpose. The newest fix is always the outermost point, so tight
  // padding would park it against the edge and the frame would re-fit on every reading.
  // At 0.6 the vehicle sits about three-quarters out and has room to travel visibly.
  const padLat = latSpan * 0.6
  const padLng = lngSpan * 0.6
  const midLat = (minLat + maxLat) / 2
  const midLng = (minLng + maxLng) / 2
  return {
    minLat: midLat - latSpan / 2 - padLat,
    maxLat: midLat + latSpan / 2 + padLat,
    minLng: midLng - lngSpan / 2 - padLng,
    maxLng: midLng + lngSpan / 2 + padLng,
  }
}

/** Zoom about the middle of the current view, with sane stops at both ends. */
function scale(bounds: Bounds, factor: number): Bounds {
  const midLat = (bounds.minLat + bounds.maxLat) / 2
  const midLng = (bounds.minLng + bounds.maxLng) / 2
  const latSpan = Math.min(Math.max((bounds.maxLat - bounds.minLat) * factor, 0.02), 40)
  const lngSpan = Math.min(Math.max((bounds.maxLng - bounds.minLng) * factor, 0.02), 40)
  return {
    minLat: midLat - latSpan / 2, maxLat: midLat + latSpan / 2,
    minLng: midLng - lngSpan / 2, maxLng: midLng + lngSpan / 2,
  }
}

/** Grid lines at a spacing that stays readable whether the view is a country or a town. */
function ticks(min: number, max: number): number[] {
  const span = max - min
  const step = span > 8 ? 2 : span > 4 ? 1 : span > 2 ? 0.5 : span > 0.8 ? 0.25 : span > 0.3 ? 0.1 : 0.05
  const out: number[] = []
  for (let value = Math.ceil(min / step) * step; value < max; value += step) out.push(Number(value.toFixed(2)))
  return out
}

const label = (value: number, axis: 'N' | 'E') =>
  `${Number.isInteger(value) ? value : Number(value.toFixed(2))}°${axis}`

/**
 * Fleet status counts, floating over whichever map is drawn.
 *
 * Narrow and translucent because it sits on top of the map and must hide as little of it as
 * possible. It prefers the server's counts, which cover the whole fleet, and only derives
 * them from the visible markers when none were given.
 */
function Legend({ counts, markers }: { counts?: StatusCounts; markers: MapMarker[] }) {
  const legend = counts ?? {
    live: markers.filter((m) => m.sensorStatus !== 'Offline').length,
    inTransit: markers.filter((m) => m.tripStatus === 'InTransit').length,
    delay: markers.filter((m) => m.sensorStatus === 'Warning').length,
    stopped: markers.filter((m) => m.sensorStatus === 'Offline').length,
  }
  return (
    <dl className="absolute bottom-3 left-3 z-10 w-36 overflow-hidden rounded-lg border border-line bg-surface/90 text-xs shadow-pop backdrop-blur-sm">
      {([
        ['Live', legend.live, 'bg-normal-500'],
        ['In transit', legend.inTransit, 'bg-info-500'],
        ['Delay', legend.delay, 'bg-warning-500'],
        ['Stopped', legend.stopped, 'bg-critical-500'],
      ] as const).map(([term, value, dot]) => (
        <div key={term} className="flex items-center gap-2 px-2.5 py-1">
          <span className={clsx('h-1.5 w-1.5 shrink-0 rounded-full', dot)} aria-hidden />
          <dt className="flex-1 text-ink-soft">{term}</dt>
          <dd className="tabular font-semibold">{value}</dd>
        </div>
      ))}
    </dl>
  )
}

function MapCanvas({ markers, bounds, trail, height, active, onSelect, onBoundsChange, recenterTo, counts, lockTouch = false }: {
  markers: MapMarker[]
  bounds: Bounds
  trail?: TrackPoint[]
  height: number | string
  active: MapMarker | null
  onSelect: (marker: MapMarker | null) => void
  /** Omitted on the partner portals, which get a fixed, non-interactive view. */
  onBoundsChange?: (bounds: Bounds) => void
  recenterTo?: Bounds
  counts?: StatusCounts
  /**
   * Only the expanded map claims touch gestures. On an inline map, swallowing them would
   * trap a phone user mid-page: they would swipe to scroll and pan the map instead.
   */
  lockTouch?: boolean
}) {
  const project = projector(bounds)
  const surface = useRef<HTMLDivElement>(null)
  const drag = useRef<{ x: number; y: number; from: Bounds; moved: boolean } | null>(null)

  const lats = ticks(bounds.minLat, bounds.maxLat)
  const lngs = ticks(bounds.minLng, bounds.maxLng)
  const cities = CITIES.filter((c) =>
    c.lat > bounds.minLat && c.lat < bounds.maxLat && c.lng > bounds.minLng && c.lng < bounds.maxLng)

  const path = (trail ?? [])
    .map((point) => { const p = project(point.latitude, point.longitude); return `${p.x},${p.y}` })
    .join(' ')

  function onPointerDown(event: React.PointerEvent) {
    if (!onBoundsChange) return
    if (event.pointerType === 'touch' && !lockTouch) return
    drag.current = { x: event.clientX, y: event.clientY, from: bounds, moved: false }
  }

  function onPointerMove(event: React.PointerEvent) {
    const state = drag.current
    if (!state || !onBoundsChange || !surface.current) return
    const box = surface.current.getBoundingClientRect()
    const dx = (event.clientX - state.x) / box.width
    const dy = (event.clientY - state.y) / box.height
    if (Math.abs(dx) + Math.abs(dy) < 0.004) return
    state.moved = true
    // Dragging the map right reveals what lies to the west; dragging down reveals the north.
    const lngShift = -dx * (state.from.maxLng - state.from.minLng)
    const latShift = dy * (state.from.maxLat - state.from.minLat)
    onBoundsChange({
      minLng: state.from.minLng + lngShift, maxLng: state.from.maxLng + lngShift,
      minLat: state.from.minLat + latShift, maxLat: state.from.maxLat + latShift,
    })
  }

  const endDrag = () => { drag.current = null }

  return (
    <div
      ref={surface}
      className={clsx(
        'relative h-full overflow-hidden bg-[#EEF3F6]',
        lockTouch && 'touch-none',
        onBoundsChange && 'cursor-grab active:cursor-grabbing',
      )}
      style={{ height }}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerLeave={endDrag}
    >
      <svg className="absolute inset-0 h-full w-full" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden>
        {lats.map((lat) => {
          const y = project(lat, bounds.minLng).y
          return <line key={`lat${lat}`} x1="0" x2="100" y1={y} y2={y} stroke="#DEE6EC" strokeWidth="0.3" vectorEffect="non-scaling-stroke" />
        })}
        {lngs.map((lng) => {
          const x = project(bounds.minLat, lng).x
          return <line key={`lng${lng}`} y1="0" y2="100" x1={x} x2={x} stroke="#DEE6EC" strokeWidth="0.3" vectorEffect="non-scaling-stroke" />
        })}
        {path && (
          <>
            <polyline points={path} fill="none" stroke="#FFFFFF" strokeWidth="5" strokeLinecap="round"
              strokeLinejoin="round" vectorEffect="non-scaling-stroke" opacity="0.9" />
            <polyline points={path} fill="none" stroke="#5880E4" strokeWidth="2.5" strokeLinecap="round"
              strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
          </>
        )}
      </svg>

      {/* The topmost latitude label would sit on the longitude row in the corner. */}
      {lats.filter((lat) => project(lat, bounds.minLng).y > 6).map((lat) => (
        <span key={`latl${lat}`} className="pointer-events-none absolute right-2 hidden -translate-y-1/2 text-[10px] text-ink-faint sm:block"
          style={{ top: `${project(lat, bounds.minLng).y}%` }}>{label(lat, 'N')}</span>
      ))}
      {lngs.map((lng) => (
        <span key={`lngl${lng}`} className="pointer-events-none absolute top-1.5 -translate-x-1/2 text-[10px] text-ink-faint"
          style={{ left: `${project(bounds.minLat, lng).x}%` }}>{label(lng, 'E')}</span>
      ))}

      {/*
        The name runs to the left of its dot, ending at the coordinate. A vehicle sits on
        the point with its pill below, so keeping town names out of that column stops the
        two colliding wherever a truck is near a city — which is most of the time.
      */}
      {cities.map((city) => {
        const point = project(city.lat, city.lng)
        return (
          <span
            key={city.name}
            className="pointer-events-none absolute flex items-center gap-1 whitespace-nowrap text-[11px] text-ink-faint"
            style={{ left: `${point.x}%`, top: `${point.y}%`, transform: 'translate(-100%, -50%)' }}
          >
            {city.name}
            <span className="inline-block h-1 w-1 rounded-full bg-ink-faint" />
          </span>
        )
      })}

      {/* Where the trail began, so the direction of travel is obvious. */}
      {trail && trail.length > 1 && (() => {
        const start = project(trail[0].latitude, trail[0].longitude)
        return (
          <span
            className="pointer-events-none absolute flex -translate-x-1/2 items-center gap-1 whitespace-nowrap"
            style={{ left: `${start.x}%`, top: `calc(${start.y}% + 10px)` }}
          >
            <span className="h-2 w-2 rounded-full border-2 border-info-500 bg-surface" />
            <span className="rounded bg-surface/90 px-1 text-[10px] text-ink-faint">
              from here, {since(trail[0].recordedAt)}
            </span>
          </span>
        )
      })()}

      {markers.map((marker) => {
        const point = project(marker.latitude, marker.longitude)
        if (point.x < -5 || point.x > 105 || point.y < -5 || point.y > 105) return null
        const palette = tone(marker.sensorStatus)
        const selected = active?.tripId === marker.tripId
        return (
          <button
            key={marker.tripId}
            className="absolute flex -translate-x-1/2 -translate-y-1/2 flex-col items-center gap-1 focus:outline-none"
            style={{ left: `${point.x}%`, top: `${point.y}%` }}
            onClick={() => { if (!drag.current?.moved) onSelect(selected ? null : marker) }}
            aria-label={`${marker.fleetNumber}, ${marker.sensorStatus}`}
          >
            <span className="relative flex">
              {marker.sensorStatus === 'Critical' && (
                <span className={clsx('absolute -inset-1 animate-ring rounded-full', palette.dot, 'opacity-50')} />
              )}
              <Truck size={22} weight="duotone" className={clsx('relative drop-shadow-sm', palette.ink)} />
            </span>
            <span
              className={clsx(
                'tabular rounded-md border px-1.5 py-0.5 text-[11px] font-medium leading-tight shadow-sm transition',
                selected
                  ? clsx(palette.fill, 'border-transparent text-white')
                  : clsx('border bg-surface/95', palette.edge, palette.ink),
              )}
            >
              {marker.fleetNumber}
            </span>
          </button>
        )
      })}

      {active && (
        <div
          className="absolute z-20 w-64 -translate-x-1/2 rounded-lg border border-line bg-surface p-3 shadow-pop"
          style={{
            left: `${Math.min(Math.max(project(active.latitude, active.longitude).x, 18), 82)}%`,
            top: `${Math.min(project(active.latitude, active.longitude).y + 6, 72)}%`,
          }}
        >
          <div className="flex items-start justify-between gap-2">
            <div>
              <p className="text-sm font-medium">{active.fleetNumber} <span className="font-normal text-ink-soft">{active.vehicleCode}</span></p>
              <p className="text-xs text-ink-soft">{active.route}</p>
            </div>
            <span className={clsx('mt-1 h-2.5 w-2.5 rounded-full', tone(active.sensorStatus).dot)} />
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
            <dt className="text-ink-faint">Position</dt>
            <dd className="text-right font-medium">{active.latitude.toFixed(3)}, {active.longitude.toFixed(3)}</dd>
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

      {markers.length === 0 && (
        <p className="pointer-events-none absolute inset-0 grid place-items-center text-sm text-ink-faint">
          No positions yet. Trucks appear here once a trip starts.
        </p>
      )}

      <Legend counts={counts} markers={markers} />

      {onBoundsChange && (
        <div className="absolute right-4 top-10 z-10 flex flex-col items-center gap-2">
          <div className="overflow-hidden rounded-lg border border-line bg-surface shadow-pop">
            <button
              className="grid h-8 w-8 place-items-center text-ink-soft transition hover:bg-surface-sunken hover:text-ink"
              onClick={() => onBoundsChange(scale(bounds, 0.6))}
              aria-label="Zoom in"
            >
              <Plus size={15} />
            </button>
            <button
              className="grid h-8 w-8 place-items-center border-t border-line text-ink-soft transition hover:bg-surface-sunken hover:text-ink"
              onClick={() => onBoundsChange(scale(bounds, 1 / 0.6))}
              aria-label="Zoom out"
            >
              <Minus size={15} />
            </button>
          </div>
          {recenterTo && (
            <button
              className="grid h-8 w-8 place-items-center rounded-lg border border-line bg-surface text-ink-soft shadow-pop transition hover:bg-surface-sunken hover:text-ink"
              onClick={() => onBoundsChange(recenterTo)}
              aria-label="Centre on the fleet"
              title="Centre on the fleet"
            >
              <Crosshair size={15} />
            </button>
          )}
        </div>
      )}
    </div>
  )
}

export function FleetMap({ markers, height = 380, expandable = false, counts }: {
  markers: MapMarker[]
  height?: number
  /**
   * Admin screens only. Panning, zooming and the expanded view all belong to the admin
   * app; a partner's tracking link gets a fixed view and cannot call the admin API.
   */
  expandable?: boolean
  counts?: StatusCounts
}) {
  const [active, setActive] = useState<MapMarker | null>(null)
  const [expanded, setExpanded] = useState(false)
  /**
   * Google when a key is configured, the coordinate grid otherwise — and also the grid if
   * the key turns out to be unusable at runtime (wrong referrer, API not enabled, no quota),
   * so a misconfiguration degrades to a working map instead of an empty panel.
   */
  const [googleFailed, setGoogleFailed] = useState<string | null>(null)
  const useGoogle = hasGoogleMapsKey && !googleFailed

  const plotted = useMemo(
    () => markers.filter((marker) => Number.isFinite(marker.latitude) && Number.isFinite(marker.longitude)),
    [markers],
  )

  // With one vehicle reporting, its trail is the only one worth drawing; with several,
  // follow whichever the operator picked.
  const followed = active ?? (plotted.length === 1 ? plotted[0] : null)
  const track = useTripTrack(followed?.tripId, 12, expandable && expanded)
  const points = track.data?.track ?? []

  const [inlineView, setInlineView] = useState<Bounds>(NIGERIA)

  /**
   * The expanded frame is set when the map opens and then held still.
   *
   * Re-fitting on every reading looks wrong: the newest fix is always the outermost
   * point, so padding pins it to the same spot on screen and the vehicle appears parked
   * while the country slides beneath it. Holding the frame lets the marker actually
   * travel across it. Once the operator pans or zooms, the frame is theirs and auto-fit
   * stops entirely until they press the centre button.
   */
  const [frame, setFrame] = useState<Bounds | null>(null)
  const [steered, setSteered] = useState(false)

  useEffect(() => {
    if (!expanded) { setFrame(null); setSteered(false); return }
    if (steered) return
    const all = [...plotted, ...points]
    if (all.length === 0) return
    setFrame((current) => {
      if (!current) return fit(all)
      const inside = all.every((p) =>
        p.latitude > current.minLat && p.latitude < current.maxLat &&
        p.longitude > current.minLng && p.longitude < current.maxLng)
      return inside ? current : fit(all)
    })
  }, [expanded, steered, plotted, points])

  const expandedBounds = expanded ? frame ?? fit([...plotted, ...points]) : NIGERIA

  return (
    <div className="relative">
      {useGoogle ? (
        <div className="relative" style={{ height }}>
          <GoogleFleetMap markers={plotted} height={height} onSelect={setActive} onUnavailable={setGoogleFailed} />
          <Legend counts={counts} markers={plotted} />
        </div>
      ) : (
        <MapCanvas
          markers={plotted}
          bounds={inlineView}
          height={height}
          active={expanded ? null : active}
          onSelect={setActive}
          counts={counts}
          onBoundsChange={expandable ? setInlineView : undefined}
          recenterTo={expandable ? (plotted.length > 0 ? fit(plotted) : NIGERIA) : undefined}
        />
      )}

      {expandable && (
        <button
          className="absolute right-4 top-3 z-10 rounded-md border border-line bg-surface/95 px-2.5 py-1 text-[13px] font-medium text-brand-600 shadow-sm transition hover:bg-surface"
          onClick={() => setExpanded(true)}
        >
          <span className="flex items-center gap-1.5"><Maximize2 size={13} /> Full view</span>
        </button>
      )}

      <Modal
        open={expanded}
        title={followed ? `${followed.fleetNumber} · ${followed.route}` : 'Fleet positions'}
        description={
          followed
            ? 'Zoomed to this vehicle, with the last 12 hours of fixes behind it. Drag to pan; it keeps updating as readings arrive.'
            : 'Zoomed to the vehicles reporting. Select one to draw where it has been.'
        }
        width="max-w-6xl"
        bodyClassName="p-0"
        onClose={() => setExpanded(false)}
      >
        {useGoogle ? (
          <div className="relative" style={{ height: 'min(70vh, 720px)' }}>
            <GoogleFleetMap
              markers={plotted}
              trail={points}
              height="min(70vh, 720px)"
              satellite
              onSelect={setActive}
              onUnavailable={setGoogleFailed}
            />
            <Legend counts={counts} markers={plotted} />
          </div>
        ) : (
          <MapCanvas
            markers={plotted}
            bounds={expandedBounds}
            trail={points}
            height="min(70vh, 720px)"
            lockTouch
            active={active}
            onSelect={setActive}
            counts={counts}
            onBoundsChange={(next) => { setSteered(true); setFrame(next) }}
            recenterTo={fit([...plotted, ...points])}
          />
        )}
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line px-4 py-2.5 text-xs text-ink-soft">
          <span className="flex items-center gap-2">
            {track.isFetching ? <Spinner size={13} /> : <MapPin size={13} className="text-ink-faint" />}
            {followed
              ? points.length > 1
                ? `${points.length} fixes over the last 12 hours`
                : track.isFetching ? 'Loading the trail…' : 'Only one fix so far — the line appears as the vehicle moves'
              : `${plotted.length} vehicle${plotted.length === 1 ? '' : 's'} reporting`}
          </span>
          {followed && (
            <a
              className="btn-secondary h-7 px-2 text-xs"
              target="_blank"
              rel="noreferrer"
              href={`https://www.google.com/maps?q=${followed.latitude},${followed.longitude}`}
            >
              Open in Google Maps
            </a>
          )}
        </div>
      </Modal>
    </div>
  )
}
