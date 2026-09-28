-- When the unit last reported a usable GPS fix. A reading with lat/lng 0,0 means "no fix",
-- and the last known position is kept; without this column the UI cannot say how old it is.
ALTER TABLE devices ADD COLUMN IF NOT EXISTS last_fix_at timestamptz;

-- Best effort for units already storing a position: attribute it to their last reading
-- that actually carried coordinates.
UPDATE devices d
SET last_fix_at = (
    SELECT max(r.recorded_at) FROM sensor_readings r
    WHERE r.device_id = d.id AND r.latitude IS NOT NULL AND r.longitude IS NOT NULL)
WHERE d.last_latitude IS NOT NULL AND d.last_fix_at IS NULL;
