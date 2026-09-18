-- WonderFleet V002 — fuel planning and reconciliation.
--
-- Dispatch needs to know how much fuel to send a truck out with. The estimate depends on the
-- vehicle, the load, the route, traffic, the weather and the cooling unit, so every estimate is
-- stored with the inputs and the price that produced it.

-- ---------------------------------------------------------------- vehicles
ALTER TABLE vehicles
    ADD COLUMN fuel_type varchar(20) NOT NULL DEFAULT 'Diesel'
        CONSTRAINT ck_vehicles_fuel_type CHECK (fuel_type IN ('Diesel', 'Petrol')),
    ADD COLUMN tank_capacity_litres numeric(8, 2)
        CONSTRAINT ck_vehicles_tank CHECK (tank_capacity_litres IS NULL OR tank_capacity_litres BETWEEN 10 AND 2000),
    ADD COLUMN baseline_consumption_l_per_100km numeric(6, 2)
        CONSTRAINT ck_vehicles_baseline CHECK (baseline_consumption_l_per_100km IS NULL OR baseline_consumption_l_per_100km BETWEEN 3 AND 120);

COMMENT ON COLUMN vehicles.baseline_consumption_l_per_100km IS
    'Measured consumption from the partner''s fuel logs; overrides the modelled baseline.';

-- ---------------------------------------------------------------- trips
ALTER TABLE trips
    ADD COLUMN planned_fuel_litres numeric(10, 2)
        CONSTRAINT ck_trips_planned_fuel CHECK (planned_fuel_litres IS NULL OR planned_fuel_litres >= 0),
    ADD COLUMN planned_fuel_cost numeric(14, 2)
        CONSTRAINT ck_trips_planned_fuel_cost CHECK (planned_fuel_cost IS NULL OR planned_fuel_cost >= 0),
    ADD COLUMN actual_fuel_litres numeric(10, 2)
        CONSTRAINT ck_trips_actual_fuel CHECK (actual_fuel_litres IS NULL OR (actual_fuel_litres > 0 AND actual_fuel_litres <= 5000)),
    ADD COLUMN actual_fuel_cost numeric(14, 2)
        CONSTRAINT ck_trips_actual_fuel_cost CHECK (actual_fuel_cost IS NULL OR actual_fuel_cost >= 0),
    ADD COLUMN fuel_recorded_at timestamptz;

-- ---------------------------------------------------------------- pump prices
CREATE TABLE fuel_prices (
    fuel_type           varchar(20)  PRIMARY KEY
        CONSTRAINT ck_fuel_prices_type CHECK (fuel_type IN ('Diesel', 'Petrol')),
    price_per_litre     numeric(10, 2) NOT NULL
        CONSTRAINT ck_fuel_prices_value CHECK (price_per_litre > 0 AND price_per_litre <= 100000),
    currency            varchar(3)   NOT NULL DEFAULT 'NGN',
    source              varchar(200),
    updated_by_admin_id uuid         REFERENCES admin_users (id) ON DELETE SET NULL,
    updated_at          timestamptz  NOT NULL DEFAULT now()
);

COMMENT ON TABLE fuel_prices IS 'Pump price per fuel. Estimates copy the price they used so history stays truthful.';

-- ---------------------------------------------------------------- stored estimates
CREATE TABLE fuel_estimates (
    id                  uuid PRIMARY KEY,
    trip_id             uuid REFERENCES trips (id) ON DELETE CASCADE,
    vehicle_id          uuid REFERENCES vehicles (id) ON DELETE SET NULL,
    fuel_type           varchar(20) NOT NULL
        CONSTRAINT ck_fuel_estimates_type CHECK (fuel_type IN ('Diesel', 'Petrol')),

    distance_km         double precision NOT NULL CONSTRAINT ck_fuel_estimates_distance CHECK (distance_km >= 0),
    duration_minutes    integer     NOT NULL DEFAULT 0,
    free_flow_minutes   integer,
    traffic_ratio       double precision NOT NULL DEFAULT 1
        CONSTRAINT ck_fuel_estimates_traffic CHECK (traffic_ratio >= 1 AND traffic_ratio <= 5),
    avg_ambient_c       double precision,
    peak_ambient_c      double precision,
    refrigerated        boolean     NOT NULL DEFAULT false,

    base_litres         numeric(10, 2) NOT NULL,
    drive_litres        numeric(10, 2) NOT NULL,
    reefer_litres       numeric(10, 2) NOT NULL DEFAULT 0,
    idle_litres         numeric(10, 2) NOT NULL DEFAULT 0,
    total_litres        numeric(10, 2) NOT NULL,
    recommended_litres  numeric(10, 2) NOT NULL,
    litres_per100km     numeric(8, 2)  NOT NULL DEFAULT 0,
    co2_kg              numeric(10, 2) NOT NULL DEFAULT 0,

    price_per_litre     numeric(10, 2) NOT NULL,
    estimated_cost      numeric(14, 2) NOT NULL,
    currency            varchar(3)  NOT NULL DEFAULT 'NGN',

    confidence          varchar(10) NOT NULL DEFAULT 'Medium'
        CONSTRAINT ck_fuel_estimates_confidence CHECK (confidence IN ('Low', 'Medium', 'High')),
    breakdown_json      jsonb       NOT NULL DEFAULT '[]'::jsonb,
    assumptions_json    jsonb       NOT NULL DEFAULT '[]'::jsonb,

    created_by_admin_id uuid REFERENCES admin_users (id) ON DELETE SET NULL,
    created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_fuel_estimates_trip ON fuel_estimates (trip_id, created_at DESC);
CREATE INDEX ix_fuel_estimates_created ON fuel_estimates (created_at DESC);

-- Prototype pump prices (Nigeria, mid-2026). Administrators keep these current in Settings.
INSERT INTO fuel_prices (fuel_type, price_per_litre, currency, source)
VALUES ('Diesel', 1150.00, 'NGN', 'Prototype default — update in Settings'),
       ('Petrol', 950.00, 'NGN', 'Prototype default — update in Settings');
