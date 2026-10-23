import { useEffect, useRef, useState } from 'react'
import { loadGoogleMaps, onGoogleMapsAuthFailure, truckIcon } from '@/lib/googleMaps'
import { humidity as humidityText, since, temperature as temperatureText } from '@/lib/format'
import type { MapMarker, TrackPoint } from '@/lib/types'

/**
 * The fleet on a real Google map — roads, satellite imagery and all.
 *
 * It renders only when a Maps JavaScript API key is configured; the caller falls back to the
 * coordinate grid otherwise, and also if loading fails, so a wrong key or a blocked referrer
 * degrades to a working map rather than an empty box.
 *
 * Markers are reused between updates rather than torn down and rebuilt. Recreating them on
 * every reading makes the trucks blink and loses any open info window, which defeats the
 * point of watching a vehicle move.
 */

const COLOUR: Record<string, string> = {
  Normal: '#2F9E44',
  Warning: '#C08A2C',
  Critical: '#D64545',
  Offline: '#8A99A8',
  Idle: '#8A99A8',
}

const escapeHtml = (value: string) =>
  value.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]!))

function infoHtml(marker: MapMarker) {
  const row = (term: string, value: string) =>
    `<div style="display:flex;justify-content:space-between;gap:16px"><span style="color:#687C88">${term}</span><span style="font-weight:600">${escapeHtml(value)}</span></div>`
  return `<div style="font-family:inherit;font-size:12.5px;line-height:1.7;min-width:210px">
    <div style="font-weight:700;font-size:14px;color:#19233F">${escapeHtml(marker.fleetNumber)}</div>
    <div style="color:#687C88;margin-bottom:6px">${escapeHtml(marker.route)}</div>
    ${row('Driver', marker.driverName ?? '—')}
    ${row('Temperature', temperatureText(marker.temperature))}
    ${row('Humidity', humidityText(marker.humidity))}
    ${row('Speed', marker.speedKmh == null ? '—' : `${marker.speedKmh.toFixed(0)} km/h`)}
    ${row('Position', `${marker.latitude.toFixed(4)}, ${marker.longitude.toFixed(4)}`)}
    ${row('Last fix', since(marker.lastPositionAt) || '—')}
    <a href="/trips/${encodeURIComponent(marker.tripId)}" style="display:inline-block;margin-top:8px;color:#63883C;font-weight:600;text-decoration:none">View shipment →</a>
  </div>`
}

export function GoogleFleetMap({ markers, trail, height, onSelect, onUnavailable, satellite = false }: {
  markers: MapMarker[]
  trail?: TrackPoint[]
  height: number | string
  onSelect?: (marker: MapMarker | null) => void
  /** Told once if the API cannot load, so the caller can swap in the grid map. */
  onUnavailable?: (reason: string) => void
  satellite?: boolean
}) {
  const host = useRef<HTMLDivElement>(null)
  const map = useRef<google.maps.Map | null>(null)
  const pins = useRef(new Map<string, google.maps.Marker>())
  const line = useRef<google.maps.Polyline | null>(null)
  const info = useRef<google.maps.InfoWindow | null>(null)
  const framed = useRef(false)
  const [ready, setReady] = useState(false)

  // A rejected key surfaces here, not as a load error, so the caller can swap in the grid
  // instead of leaving Google's own error panel on the page.
  useEffect(() => onGoogleMapsAuthFailure((reason) => onUnavailable?.(reason)), [onUnavailable])

  // --- create the map once
  useEffect(() => {
    let cancelled = false
    loadGoogleMaps()
      .then((maps) => {
        if (cancelled || !host.current) return
        map.current = new maps.Map(host.current, {
          center: { lat: 9.0765, lng: 7.3986 },
          zoom: 6,
          mapTypeId: satellite ? 'hybrid' : 'roadmap',
          mapTypeControl: true,
          streetViewControl: false,
          fullscreenControl: false,
          gestureHandling: 'greedy',
        })
        info.current = new maps.InfoWindow()
        setReady(true)
      })
      .catch((error: Error) => { if (!cancelled) onUnavailable?.(error.message) })
    return () => { cancelled = true }
    // Deliberately once: re-creating the map on every prop change would reset the view.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // --- markers follow the feed
  useEffect(() => {
    const maps = window.google?.maps
    if (!ready || !map.current || !maps) return

    const seen = new Set<string>()
    for (const marker of markers) {
      if (!Number.isFinite(marker.latitude) || !Number.isFinite(marker.longitude)) continue
      seen.add(marker.tripId)
      const position = { lat: marker.latitude, lng: marker.longitude }
      const colour = COLOUR[marker.sensorStatus] ?? COLOUR.Offline
      const existing = pins.current.get(marker.tripId)

      if (existing) {
        existing.setPosition(position)
        existing.setIcon({ url: truckIcon(colour), scaledSize: new maps.Size(40, 46), anchor: new maps.Point(20, 44), labelOrigin: new maps.Point(20, 54) })
        existing.setLabel({ text: marker.fleetNumber, color: '#19233F', fontSize: '11px', fontWeight: '600' })
        continue
      }

      const pin = new maps.Marker({
        position,
        map: map.current,
        title: `${marker.fleetNumber} · ${marker.sensorStatus}`,
        icon: { url: truckIcon(colour), scaledSize: new maps.Size(40, 46), anchor: new maps.Point(20, 44), labelOrigin: new maps.Point(20, 54) },
        label: { text: marker.fleetNumber, color: '#19233F', fontSize: '11px', fontWeight: '600' },
      })
      pin.addListener('click', () => {
        info.current?.setContent(infoHtml(marker))
        info.current?.open({ anchor: pin, map: map.current! })
        onSelect?.(marker)
      })
      pins.current.set(marker.tripId, pin)
    }

    for (const [id, pin] of pins.current) {
      if (seen.has(id)) continue
      pin.setMap(null)
      pins.current.delete(id)
    }

    // Frame once. After that the view belongs to whoever is using it.
    if (!framed.current && markers.length > 0) {
      const bounds = new maps.LatLngBounds()
      for (const marker of markers) bounds.extend({ lat: marker.latitude, lng: marker.longitude })
      for (const point of trail ?? []) bounds.extend({ lat: point.latitude, lng: point.longitude })
      map.current.fitBounds(bounds, 64)
      if (markers.length === 1 && (trail?.length ?? 0) < 2) map.current.setZoom(13)
      framed.current = true
    }
  }, [ready, markers, trail, onSelect])

  // --- the breadcrumb
  useEffect(() => {
    const maps = window.google?.maps
    if (!ready || !map.current || !maps) return
    const path = (trail ?? []).map((point) => ({ lat: point.latitude, lng: point.longitude }))

    if (path.length < 2) {
      line.current?.setMap(null)
      line.current = null
      return
    }
    if (line.current) { line.current.setPath(path); return }
    line.current = new maps.Polyline({
      path, map: map.current, strokeColor: '#5880E4', strokeOpacity: 0.95, strokeWeight: 4,
    })
  }, [ready, trail])

  return <div ref={host} style={{ height }} className="h-full w-full bg-[#EEF3F6]" />
}
