/**
 * Loads the Maps JavaScript API once per page, on demand.
 *
 * The key is baked in at build time, so it is visible to anyone who opens the site — that is
 * normal for this API and is why Google restricts it by HTTP referrer rather than by secrecy.
 * Restrict it to wonderfleet.org in the Cloud console; it is not the same key as the server's.
 *
 * With no key the promise rejects immediately and the caller draws the coordinate grid
 * instead, so the map is never blank and never blocks on a network call that cannot succeed.
 */
const KEY = (import.meta.env.VITE_GOOGLE_MAPS_API_KEY ?? '').trim()

export const hasGoogleMapsKey = KEY.length > 0

let pending: Promise<typeof google.maps> | null = null

declare global {
  interface Window { gm_authFailure?: () => void }
}

type AuthListener = (reason: string) => void
const authListeners = new Set<AuthListener>()

/**
 * Google rejects a key *after* the script has loaded successfully.
 *
 * A wrong HTTP referrer, disabled billing or an un-enabled Maps JavaScript API all load
 * fine and then paint Google's own grey "Oops! Something went wrong" panel over the map.
 * The script's load event has already fired by then, so nothing else notices. This global
 * is the documented hook Google calls in that case, and it is how the app finds out it
 * should draw the coordinate grid instead.
 */
if (typeof window !== 'undefined') {
  window.gm_authFailure = () => {
    const reason = 'Google rejected this key for this site. Check the key\'s HTTP referrer '
      + 'restriction (it must list this exact hostname, www included), that the Maps '
      + 'JavaScript API is enabled, and that billing is active.'
    for (const listener of authListeners) listener(reason)
  }
}

/** Subscribes to that rejection; returns an unsubscribe function. */
export function onGoogleMapsAuthFailure(listener: AuthListener): () => void {
  authListeners.add(listener)
  return () => { authListeners.delete(listener) }
}

export function loadGoogleMaps(): Promise<typeof google.maps> {
  if (!hasGoogleMapsKey) return Promise.reject(new Error('No Maps JavaScript API key is configured.'))
  if (typeof window !== 'undefined' && window.google?.maps) return Promise.resolve(window.google.maps)
  if (pending) return pending

  pending = new Promise((resolve, reject) => {
    const existing = document.getElementById('wf-google-maps') as HTMLScriptElement | null
    const script = existing ?? document.createElement('script')
    script.id = 'wf-google-maps'
    script.async = true
    if (!existing) {
      script.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(KEY)}&v=weekly&loading=async`
      document.head.appendChild(script)
    }
    script.addEventListener('load', () => {
      if (window.google?.maps) resolve(window.google.maps)
      else reject(new Error('Google Maps loaded but exposed no API.'))
    })
    script.addEventListener('error', () => {
      pending = null
      reject(new Error('Google Maps could not be loaded. Check the key and its referrer restrictions.'))
    })
  })
  return pending
}

/** A truck pin in the status colour, drawn as a data URI so no image hosting is needed. */
export function truckIcon(colour: string): string {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="40" height="46" viewBox="0 0 40 46">
    <ellipse cx="20" cy="43" rx="6" ry="2.5" fill="rgba(15,23,42,0.22)"/>
    <path d="M20 2c7.2 0 13 5.6 13 12.6 0 8.9-9.9 19.3-12.3 21.7a1 1 0 0 1-1.4 0C16.9 33.9 7 23.5 7 14.6 7 7.6 12.8 2 20 2z" fill="${colour}"/>
    <path d="M20 2c7.2 0 13 5.6 13 12.6 0 8.9-9.9 19.3-12.3 21.7a1 1 0 0 1-1.4 0C16.9 33.9 7 23.5 7 14.6 7 7.6 12.8 2 20 2z" fill="none" stroke="rgba(255,255,255,0.85)" stroke-width="1.6"/>
    <g fill="#fff">
      <rect x="12" y="10.5" width="9" height="7" rx="1.2"/>
      <path d="M21.6 12.4h3.1l2.7 2.9v2.2h-5.8z"/>
      <circle cx="15.2" cy="19.2" r="1.9"/>
      <circle cx="24.4" cy="19.2" r="1.9"/>
    </g>
  </svg>`
  return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`
}
