namespace WonderFleet.Domain.Enums;

public enum AdminRole { SuperAdmin, Admin }

public enum PartnerStatus { Pending, Active, Suspended }

public enum AvailabilityStatus { AvailableNow, AvailableSoon, FullyBooked, Unavailable }

public enum InsuranceCoverageType { GoodsInTransit, ComprehensiveFleet, ThirdParty, MarineCargo, Other }

public enum PartnerDocumentType { CacCertificate, InsurancePolicy, RegisteredDrivers, VehicleLicense, Other }

public enum DriverStatus { Available, OnTrip, OffDuty }

public enum VehicleStatus { Active, Maintenance, Inactive }

/// Road fuels used by the fleet. Prices are held per fuel in fuel_prices.
public enum FuelType { Diesel, Petrol }

public enum DeviceKind { Master, SubUnit }

public enum PackagingType { Crates, Sacks, Cartons, Baskets, Pallets, ReeferContainer, Bulk, Other }

/// Lifecycle of a shipment (the UI calls a registered vehicle + shipment a "fleet").
public enum TripStatus { Scheduled, InTransit, Delayed, Stopped, Completed, Cancelled }

/// Cargo-condition badge: Normal / Warning / Critical / Offline.
public enum SensorStatus { Normal, Warning, Critical, Offline }

public enum TelemetrySource { Firebase, Webhook }

public enum AlertType { TemperatureBreach, LowTemperature, HighHumidity, LowHumidity, LowBattery, Stoppage, DeviceOffline, Delay }

public enum AlertSeverity { Informational, Warning, Critical }

public enum AlertStatus { Active, Acknowledged, Resolved }

public enum NotificationChannel { Email, Sms }

public enum NotificationCategory { Hardware, Critical, Partner, System }

public enum DeliveryStatus { Pending, Sending, Sent, Failed }

public enum ShareAudience { LogisticsPartner, AgroProcessor }

public enum ReportType { Temperature, Humidity, Co2Emission, MonthlyCompliance, SensorTransmissionLog, RouteSummary, FuelUsage }

public enum ActorType { Admin, ShareLink, System, Device }
