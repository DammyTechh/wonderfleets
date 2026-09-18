import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import type {
  Alert, AlertActivity, AlertRule, AnalyticsOverview, ChannelSetting, Dashboard, Device, Driver,
  FleetRow, FuelPrice, LiveTracking, NotificationItem, Paged, PartnerListItem, PartnerProfile, ProcessorListItem, ProduceType,
  ReportCatalogItem, RouteRecommendation, ShareLink, ShareLinkCreated, TripDetail, TripSummary,
} from './types'

const get = async <T,>(url: string, params?: Record<string, unknown>) =>
  (await api.get<T>(url, { params })).data

export const keys = {
  dashboard: ['dashboard'] as const,
  fleet: (params: unknown) => ['fleet', params] as const,
  trips: (params: unknown) => ['trips', params] as const,
  trip: (id: string) => ['trip', id] as const,
  tracking: ['tracking'] as const,
  partners: (params: unknown) => ['partners', params] as const,
  partner: (id: string) => ['partner', id] as const,
  processors: (params: unknown) => ['processors', params] as const,
  drivers: (params: unknown) => ['drivers', params] as const,
  devices: (params: unknown) => ['devices', params] as const,
  produce: ['produce'] as const,
  alerts: (params: unknown) => ['alerts', params] as const,
  alertSummary: ['alerts', 'summary'] as const,
  alertRules: ['alerts', 'rules'] as const,
  channels: ['alerts', 'channels'] as const,
  activity: (days: number) => ['alerts', 'activity', days] as const,
  notifications: (params: unknown) => ['notifications', params] as const,
  unread: ['notifications', 'unread'] as const,
  analytics: (params: unknown) => ['analytics', params] as const,
  reports: (month?: string) => ['reports', month] as const,
  shareLinks: (params: unknown) => ['share-links', params] as const,
  routeAi: ['route-ai'] as const,
}

export const useDashboard = () =>
  useQuery({ queryKey: keys.dashboard, queryFn: () => get<Dashboard>('/dashboard'), refetchInterval: 30_000 })

export const useFleet = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.fleet(params), queryFn: () => get<Paged<FleetRow>>('/fleet', params) })

export const useTrips = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.trips(params), queryFn: () => get<Paged<TripSummary>>('/trips', params) })

export const useTrip = (id?: string) =>
  useQuery({ queryKey: keys.trip(id ?? ''), queryFn: () => get<TripDetail>(`/trips/${id}`), enabled: Boolean(id) })

export const useLiveTracking = () =>
  useQuery({ queryKey: keys.tracking, queryFn: () => get<LiveTracking>('/tracking/live'), refetchInterval: 20_000 })

export const usePartners = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.partners(params), queryFn: () => get<Paged<PartnerListItem>>('/logistics-partners', params) })

export const usePartner = (id?: string) =>
  useQuery({ queryKey: keys.partner(id ?? ''), queryFn: () => get<PartnerProfile>(`/logistics-partners/${id}`), enabled: Boolean(id) })

export const useFuelPrices = () =>
  useQuery({ queryKey: ['fuel', 'prices'], queryFn: () => get<FuelPrice[]>('/fuel/prices'), staleTime: 300_000 })

export const useTripFuel = (tripId?: string) =>
  useQuery({
    queryKey: ['fuel', 'trip', tripId],
    queryFn: () => get<import('./types').TripFuelSummary>(`/trips/${tripId}/fuel`),
    enabled: Boolean(tripId),
  })

export const useProcessors = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.processors(params), queryFn: () => get<Paged<ProcessorListItem>>('/agro-processors', params) })

export const useDrivers = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.drivers(params), queryFn: () => get<Paged<Driver>>('/drivers', params) })

export const useDevices = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.devices(params), queryFn: () => get<Paged<Device>>('/devices', params) })

export const useProduceTypes = () =>
  useQuery({ queryKey: keys.produce, queryFn: () => get<ProduceType[]>('/produce-types'), staleTime: 300_000 })

export const useAlerts = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.alerts(params), queryFn: () => get<Paged<Alert>>('/alerts', params) })

export const useAlertSummary = () =>
  useQuery({ queryKey: keys.alertSummary, queryFn: () => get<{ critical: number; warning: number; informational: number; resolved: number; resolvedWindowDays: number }>('/alerts/summary'), refetchInterval: 60_000 })

export const useAlertRules = () => useQuery({ queryKey: keys.alertRules, queryFn: () => get<AlertRule[]>('/alerts/rules') })

export const useChannels = () => useQuery({ queryKey: keys.channels, queryFn: () => get<ChannelSetting[]>('/alerts/channels') })

export const useAlertActivity = (days = 7) =>
  useQuery({ queryKey: keys.activity(days), queryFn: () => get<AlertActivity>('/alerts/activity', { days }) })

export const useNotifications = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.notifications(params), queryFn: () => get<Paged<NotificationItem>>('/notifications', params) })

export const useUnreadCount = () =>
  useQuery({ queryKey: keys.unread, queryFn: () => get<{ unread: number }>('/notifications/unread-count'), refetchInterval: 60_000 })

export const useAnalytics = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.analytics(params), queryFn: () => get<AnalyticsOverview>('/analytics', params) })

export const useReportCatalog = (month?: string) =>
  useQuery({ queryKey: keys.reports(month), queryFn: () => get<ReportCatalogItem[]>('/reports', { month }) })

export const useShareLinks = (params: Record<string, unknown>) =>
  useQuery({ queryKey: keys.shareLinks(params), queryFn: () => get<ShareLink[]>('/share-links', params) })

export const useRouteRecommendations = () =>
  useQuery({ queryKey: keys.routeAi, queryFn: () => get<Paged<RouteRecommendation>>('/route-ai', { pageSize: 5 }) })

/** Generic mutation helper that refreshes the affected lists. */
export function useApiMutation<TInput, TResult>(
  request: (input: TInput) => Promise<TResult>,
  invalidate: readonly (readonly unknown[])[] = [],
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: request,
    onSuccess: async () => {
      await Promise.all(invalidate.map((key) => queryClient.invalidateQueries({ queryKey: key })))
    },
  })
}

export const createShareLink = (body: Record<string, unknown>) =>
  api.post<ShareLinkCreated>('/share-links', body).then((r) => r.data)
