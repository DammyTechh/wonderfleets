// Shapes mirrored from the WonderFleet API (see docs/swagger).

export type Paged<T> = {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNext: boolean
  hasPrevious: boolean
}

export type SensorStatus = 'Normal' | 'Warning' | 'Critical' | 'Offline' | 'Idle'
export type TripStatus = 'Scheduled' | 'InTransit' | 'Stopped' | 'Delayed' | 'Completed' | 'Cancelled'
export type PartnerStatus = 'Pending' | 'Active' | 'Suspended'
export type Severity = 'Critical' | 'Warning' | 'Informational'

export interface AdminProfile {
  id: string
  adminCode: string
  fullName: string
  email: string
  phoneNumber?: string | null
  address?: string | null
  photoUrl?: string | null
  role: string
  mustChangePassword: boolean
  lastLoginAt?: string | null
}

export interface AuthResult {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
  admin: AdminProfile
}

export interface Dashboard {
  activeTrips: { count: number; newThisWeek: number }
  alerts: { open: number; critical: number; warning: number }
  avgTemperature: { average: number | null; deltaFromLastHour: number | null }
  humidity: { average: number | null; percentAboveThreshold: number }
  co2Emission: { tonnes: number; changePercentThisWeek: number | null; windowDays: number }
  routeAi: { optimizationsThisWeek: number; avgSpoilageReductionPct: number; criticalAlerts: number }
  markers: MapMarker[]
  statusCounts: StatusCounts
  activeFleets: ActiveFleet[]
  sync: SyncStatus
  unreadNotifications: number
  generatedAt: string
}

export interface MapMarker {
  tripId: string
  tripCode: string
  fleetNumber: string
  vehicleCode: string
  latitude: number
  longitude: number
  tripStatus: TripStatus
  sensorStatus: SensorStatus
  driverName?: string | null
  route: string
  temperature?: number | null
  humidity?: number | null
  speedKmh?: number | null
  lastPositionAt?: string | null
}

export interface StatusCounts {
  live: number
  inTransit: number
  stopped: number
  delay: number
}

export interface SyncStatus {
  allTransmitting: boolean
  onlineDevices: number
  expectedDevices: number
  lastSyncAt: string | null
  secondsSinceSync: number
  message: string
}

export interface DeviceSummary {
  normal: number
  warning: number
  critical: number
  offline: number
}

export interface RecentPosition {
  vehicleCode: string
  route: string
  speedKmh?: number | null
  recordedAt: string
}

export interface GpsMonitor {
  activeSignals: number
  serverTimeUtc: string
  localTime: string
  timeZone: string
  recentPositions: RecentPosition[]
}

export interface AlertTrigger {
  id: string
  title: string
  severity: Severity
  vehicleCode?: string | null
  deviceSerial?: string | null
  route: string
  at: string
}

export interface LiveTracking {
  markers: MapMarker[]
  statusCounts: StatusCounts
  deviceSummary: DeviceSummary
  climate: { avgTemperature: number | null; temperatureState: string; avgHumidity: number | null; humidityState: string }
  alertTriggers: AlertTrigger[]
  gpsMonitor: GpsMonitor
  sync: SyncStatus
}

export interface TrackPoint {
  latitude: number
  longitude: number
  temperature?: number | null
  humidity?: number | null
  speedKmh?: number | null
  recordedAt: string
}

export interface ActiveFleet {
  tripId: string
  fleetNumber: string
  vehicleCode: string
  route: string
  cargoSummary: string
  temperature?: number | null
  humidity?: number | null
  sensorStatus: SensorStatus
  tripStatus: TripStatus
}

export interface FleetRow {
  vehicleId: string
  vehicleCode: string
  fleetNumber: string
  vehicleType: string
  capacityTonnes: number
  partnerId: string
  partnerName: string
  tripId?: string | null
  tripCode?: string | null
  tripStatus?: TripStatus | null
  driverName?: string | null
  driverInitials?: string | null
  route?: string | null
  temperature?: number | null
  humidity?: number | null
  status: SensorStatus
  vehicleStatus: string
}

export interface TripSummary {
  id: string
  tripCode: string
  fleetNumber: string
  vehicleCode: string
  route: string
  status: TripStatus
  sensorStatus: SensorStatus
  partnerName: string
  processorName: string
  driverName?: string | null
  produce: string[]
  estimatedWeightTonnes: number
  loadingTime: string
  expectedArrival: string
  startedAt?: string | null
  completedAt?: string | null
  temperature?: number | null
  humidity?: number | null
}

export interface Thresholds {
  minTemperature: number
  maxTemperature: number
  minHumidity: number
  maxHumidity: number
}

export interface TripDetail extends TripSummary {
  vehicleId: string
  partnerId: string
  partnerPhone: string
  processorId: string
  driverId?: string | null
  driverPhone?: string | null
  deviceId?: string | null
  deviceSerial?: string | null
  deviceOnline: boolean
  deviceBattery?: number | null
  packagingType?: string | null
  unitCount?: number | null
  additionalNotes?: string | null
  pickupAddress: string
  originLabel: string
  destinationAddress: string
  destinationLabel: string
  thresholds: Thresholds
  latitude?: number | null
  longitude?: number | null
  speedKmh?: number | null
  lastPositionAt?: string | null
  distanceTravelledKm: number
  plannedDistanceKm?: number | null
  co2EmissionKg: number
  openAlerts: number
  activeShareLinks: number
  createdAt: string
}

export interface PartnerListItem {
  id: string
  partnerCode: string
  companyName: string
  initials: string
  photoUrl?: string | null
  contactPerson: string
  email: string
  phoneNumber: string
  fleetSize: number
  location: string
  status: PartnerStatus
  availabilityStatus: string
}

export interface PartnerProfile {
  id: string
  partnerCode: string
  companyName: string
  initials: string
  photoUrl?: string | null
  status: PartnerStatus
  onboardedAt?: string | null
  contact: { contactPerson: string; location: string; phoneNumber: string; email: string }
  stats: { fleetSize: number; completedTrips: number; activeShipments: number; activeShipmentsThisWeek: number }
  company: {
    companyName: string
    cacNumber: string
    contactPerson: string
    phoneNumber: string
    email: string
    officeAddress?: string | null
    yearsOfOperation: number
    driverPoolSize: number
    availabilityStatus: string
    insuranceCoverageType?: string | null
    insuranceExpiryDate?: string | null
  }
  corridors: string[]
  documents: {
    id: string
    documentType: string
    fileName: string
    contentType: string
    sizeBytes: number
    isVerified: boolean
    expiresOn?: string | null
    uploadedAt: string
    downloadUrl: string
  }[]
  driverPool: { onTrip: number; available: number; offDuty: number; drivers: { id: string; fullName: string; initials: string; assignment?: string | null; status: string }[] }
  fleetComposition: { id: string; truckType: string; maxTonnage: number; quantity: number }[]
  recentShipments: { tripId: string; tripCode: string; fleetNumber: string; route: string; status: string; date: string }[]
  city?: string | null
  state?: string | null
}

export interface ProcessorListItem {
  id: string
  processorCode: string
  name: string
  initials: string
  photoUrl?: string | null
  contactName?: string | null
  contactEmail?: string | null
  contactPhone?: string | null
  fleetOrders: number
  location: string
  status: PartnerStatus
}

export interface Driver {
  id: string
  fullName: string
  initials: string
  phoneNumber: string
  licenseNumber?: string | null
  homeBase?: string | null
  rating: number
  completedTrips: number
  status: 'Available' | 'OnTrip' | 'OffDuty'
  logisticsPartnerId: string
  partnerName: string
  currentAssignment?: string | null
}

export interface Device {
  id: string
  serial: string
  firebaseKey: string
  kind: 'Master' | 'SubUnit'
  vehicleId?: string | null
  fleetNumber?: string | null
  batteryLevel?: number | null
  isOnline: boolean
  lastSeenAt?: string | null
  lastTemperature?: number | null
  lastHumidity?: number | null
  currentTripCode?: string | null
  currentRoute?: string | null
  sensorStatus: SensorStatus
}

export interface ProduceType {
  id: string
  name: string
  defaultMinTemperature?: number | null
  defaultMaxTemperature?: number | null
  defaultMinHumidity?: number | null
  defaultMaxHumidity?: number | null
}

export interface Alert {
  id: string
  type: string
  severity: Severity
  status: 'Active' | 'Acknowledged' | 'Resolved'
  title: string
  message: string
  deviceSerial?: string | null
  tripId?: string | null
  tripCode?: string | null
  fleetNumber?: string | null
  route?: string | null
  readingDisplay: string
  thresholdDisplay?: string | null
  triggeredAt: string
  lastTriggeredAt: string
  occurrenceCount: number
  acknowledgedAt?: string | null
  resolvedAt?: string | null
}

export interface AlertRule {
  id: string
  alertType: string
  name: string
  description: string
  isEnabled: boolean
  thresholdValue?: number | null
  durationMinutes?: number | null
  display: string
}

export interface ChannelSetting {
  id: string
  channel: 'Email' | 'Sms' | 'InApp'
  isEnabled: boolean
  criticalOnly: boolean
  display: string
}

export interface AlertActivity {
  days: string[]
  categories: string[]
  cells: { day: string; category: string; count: number; level: 'None' | 'Informational' | 'Warning' | 'Critical' }[]
}

export interface NotificationItem {
  id: string
  category: 'Hardware' | 'Critical' | 'Partner' | 'System'
  title: string
  message: string
  partnerName?: string | null
  partnerInitials?: string | null
  requiresReview: boolean
  isRead: boolean
  createdAt: string
  group: string
}

export interface Metric {
  value: number | null
  delta: number | null
  unit: string
}

export interface AnalyticsOverview {
  period: string
  from: string
  to: string
  kpis: {
    sensorUptime: Metric
    temperatureCompliance: Metric
    humidityCompliance: Metric
    estimatedSpoilage: Metric
  }
  averageTemperature: { points: SeriesPoint[]; thresholdC: number }
  averageHumidity: { points: SeriesPoint[]; safeMin: number; safeMax: number }
  tripsByPartners: { partnerId: string; partnerName: string; partnerCode: string; trips: number }[]
  deviceHealth: {
    deviceId: string
    serial: string
    route?: string | null
    batteryLevel?: number | null
    temperature?: number | null
    humidity?: number | null
    isOnline: boolean
    lastSeenAt?: string | null
    sensorStatus: SensorStatus
  }[]
  weatherIntelligence: WeatherIntelligence
}

export interface SeriesPoint {
  label: string
  bucket: string
  value: number | null
  readings: number
}

export interface WeatherIntelligence {
  tripId?: string | null
  fleetNumber?: string | null
  origin?: PointWeather | null
  destination?: PointWeather | null
  current?: PointWeather | null
}

export interface ShareLink {
  id: string
  audience: 'LogisticsPartner' | 'AgroProcessor'
  organisationName: string
  tripCount: number
  createdAt: string
  expiresAt: string
  revokedAt?: string | null
  lastAccessedAt?: string | null
  accessCount: number
  isActive: boolean
}

export interface ShareLinkCreated {
  id: string
  url: string
  audience: string
  expiresAt: string
  tripCount: number
  emailQueued: boolean
  smsQueued: boolean
}

export interface RouteRecommendation {
  id: string
  tripId?: string | null
  origin: string
  destination: string
  requestedDeparture: string
  recommendedDeparture?: string | null
  selectedRouteIndex: number
  summary: string
  heatRiskLevel: 'Low' | 'Moderate' | 'High'
  estimatedSpoilageReductionPct: number
  driverTips: string[]
  model: string
  routes: {
    index: number
    distanceKm: number
    durationMinutes: number
    description: string
    maxForecastTempC?: number | null
    avgForecastTempC?: number | null
    heatExposure: number
    selected: boolean
  }[]
  createdAt: string
}

export interface ReportCatalogItem {
  type: string
  title: string
  description: string
  defaultFormat: string
  formats: string[]
  period: string
}

export interface PortalVehicle {
  tripId: string
  vehicleCode: string
  fleetNumber: string
  driverName?: string | null
  driverInitials: string
  route: string
  tripStatus: TripStatus
  status: SensorStatus
}

export interface PortalHeader {
  organisationName: string
  audience: string
  linkExpiresAt: string
}

export interface LogisticsTracking {
  header: PortalHeader
  markers: MapMarker[]
  statusCounts: StatusCounts
  gpsMonitor: GpsMonitor
  driverPool: { onTrip: number; available: number; offDuty: number; drivers: { id: string; fullName: string; initials: string; assignment?: string | null; status: string }[] }
  actionAlerts: { title: string; message: string; severity: Severity; fleetNumber: string; at: string }[]
  weatherAlongRoutes: PointWeather[]
}

export interface AgroShipment {
  tripId: string
  tripCode: string
  fleetNumber: string
  route: string
  produce: string[]
  estimatedWeightTonnes: number
  packagingType?: string | null
  unitCount?: number | null
  tripStatus: TripStatus
  sensorStatus: SensorStatus
  temperature?: number | null
  humidity?: number | null
  minTemperature: number
  maxTemperature: number
  minHumidity: number
  maxHumidity: number
  loadingTime: string
  expectedArrival: string
  startedAt?: string | null
  distanceTravelledKm: number
  plannedDistanceKm?: number | null
  progressPercent?: number | null
  logisticsPartner: string
  logisticsPartnerPhone: string
  driverName?: string | null
}

export interface AgroTracking {
  header: PortalHeader
  markers: MapMarker[]
  statusCounts: StatusCounts
  cargoConditions: DeviceSummary
  gpsMonitor: GpsMonitor
  cargoAlerts: { title: string; message: string; severity: Severity; fleetNumber: string; at: string }[]
  destinationWeather: PointWeather[]
}

export interface PointWeather {
  label: string
  latitude: number
  longitude: number
  temperatureC: number
  relativeHumidity: number
  condition: string
  iconUri?: string | null
}

// ---- fuel -------------------------------------------------------------------
export type FuelType = 'Diesel' | 'Petrol'

export interface FuelComponent {
  key: string
  label: string
  litres: number
  percent: number
  detail: string
}

export interface FuelEstimate {
  id?: string | null
  tripId?: string | null
  fuelType: FuelType
  distanceKm: number
  durationMinutes: number
  freeFlowMinutes?: number | null
  trafficRatio: number
  trafficLabel: string
  avgAmbientC?: number | null
  peakAmbientC?: number | null
  refrigerated: boolean
  baseLitres: number
  driveLitres: number
  reeferLitres: number
  idleLitres: number
  totalLitres: number
  recommendedLitres: number
  litresPer100Km: number
  co2Kg: number
  pricePerLitre: number
  estimatedCost: number
  recommendedCost: number
  currency: string
  confidence: 'Low' | 'Medium' | 'High'
  tankFills?: number | null
  components: FuelComponent[]
  assumptions: string[]
  createdAt: string
}

export interface TripFuelSummary {
  tripId: string
  tripCode: string
  fuelType: FuelType
  plannedLitres?: number | null
  plannedCost?: number | null
  actualLitres?: number | null
  actualCost?: number | null
  varianceLitres?: number | null
  variancePercent?: number | null
  co2Kg: number
  currency: string
  recordedAt?: string | null
  estimate?: FuelEstimate | null
}

export interface FuelPrice {
  fuelType: FuelType
  pricePerLitre: number
  currency: string
  source?: string | null
  updatedAt: string
}

// ---- portal -----------------------------------------------------------------
export interface PortalSession {
  accessToken: string
  expiresAt: string
  audience: 'LogisticsPartner' | 'AgroProcessor'
  organisationName: string
  linkExpiresAt: string
  tripCount: number
}
