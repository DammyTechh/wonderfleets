-- Standing limits for a device, used when it is not carrying a shipment.
-- A trip's own limits always win while the trip is open; these cover the rest of the time,
-- and give the compliance figures something to measure a reading against.
ALTER TABLE devices
    ADD COLUMN IF NOT EXISTS min_temperature numeric(5,2),
    ADD COLUMN IF NOT EXISTS max_temperature numeric(5,2),
    ADD COLUMN IF NOT EXISTS min_humidity    numeric(5,2),
    ADD COLUMN IF NOT EXISTS max_humidity    numeric(5,2);

ALTER TABLE devices
    ADD CONSTRAINT devices_threshold_order CHECK (
        (min_temperature IS NULL OR max_temperature IS NULL OR min_temperature < max_temperature)
        AND (min_humidity IS NULL OR max_humidity IS NULL OR min_humidity < max_humidity));
