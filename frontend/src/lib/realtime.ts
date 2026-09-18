import * as signalR from '@microsoft/signalr'

type Handlers = {
  onTelemetry?: (payload: unknown) => void
  onAlert?: (payload: unknown) => void
  onNotification?: (payload: unknown) => void
}

/**
 * Connects to the WonderFleet hub. The hub pushes live telemetry and alerts;
 * pages still poll at a slow interval so a dropped socket never freezes the UI.
 */
export function connectHub(accessTokenFactory: () => string, handlers: Handlers) {
  const baseURL = import.meta.env.VITE_API_BASE_URL ?? ''
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${baseURL}/hubs/fleet`, { accessTokenFactory })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  if (handlers.onTelemetry) connection.on('telemetry', handlers.onTelemetry)
  if (handlers.onAlert) connection.on('alert', handlers.onAlert)
  if (handlers.onNotification) connection.on('notification', handlers.onNotification)

  connection.start().catch(() => {
    /* polling keeps the dashboards current if the socket cannot open */
  })

  return () => {
    void connection.stop()
  }
}
