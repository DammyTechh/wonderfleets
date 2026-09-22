import * as signalR from '@microsoft/signalr'

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

type Handlers = {
  onTelemetry?: (payload: unknown) => void
  onAlert?: (payload: unknown) => void
  onNotification?: (payload: unknown) => void
  onStateChange?: (state: LiveState) => void
}

/**
 * Connects to the WonderFleet hub, which pushes a message the moment a reading or
 * alert is stored. Pages still poll slowly underneath, so a dropped socket never
 * freezes the UI — it only makes updates slower until the socket is back.
 *
 * The token factory may be async: it is called on every (re)connect, which can be
 * long after sign-in, so it should hand back a token that has not expired.
 */
export function connectHub(accessTokenFactory: () => string | Promise<string>, handlers: Handlers) {
  const baseURL = import.meta.env.VITE_API_BASE_URL ?? ''
  const report = (state: LiveState) => handlers.onStateChange?.(state)

  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${baseURL}/hubs/fleet`, { accessTokenFactory })
    // Quick retries first, then every 30 s for as long as the page is open.
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => [0, 2000, 5000, 10000][ctx.previousRetryCount] ?? 30000 })
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  if (handlers.onTelemetry) connection.on('telemetry', handlers.onTelemetry)
  if (handlers.onAlert) connection.on('alert', handlers.onAlert)
  if (handlers.onNotification) connection.on('notification', handlers.onNotification)

  let disposed = false
  let retry: ReturnType<typeof setTimeout> | undefined

  connection.onreconnecting(() => report('reconnecting'))
  connection.onreconnected(() => report('live'))
  connection.onclose(() => {
    if (disposed) return
    report('offline')
    schedule()
  })

  // The first connect is not covered by automatic reconnect, so retry it ourselves.
  const start = () => {
    if (disposed) return
    report('connecting')
    connection
      .start()
      .then(() => report('live'))
      .catch(() => {
        report('offline')
        schedule()
      })
  }
  const schedule = () => {
    if (disposed) return
    clearTimeout(retry)
    retry = setTimeout(start, 15000)
  }

  start()

  return () => {
    disposed = true
    clearTimeout(retry)
    void connection.stop()
  }
}
