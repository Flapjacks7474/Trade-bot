# Vehicle Specification — status: NOT IMPLEMENTED (Milestone 4)

## Data model (planned)
- **VehicleDefinition** (immutable content): id, displayName, class (Muscle, Tuner, LuxurySedan, SUVTruck, Exotic, Classic,
  Lowrider, Motorcycle, Commercial), drivetrain, mass, enginePower, maxSpeed, acceleration, braking, steeringResponse,
  grip, traction, durability, passengerCapacity, baseValue, rarity, prefabKey, upgradeSlots.
- **VehicleInstance** (save data): ownershipId, definitionId, damage, mileage, installedUpgrades, customization (paint,
  wrap, decals, wheels, tires, suspension, ride height, engine, transmission, brakes, exhaust, interior, lights, plate,
  crew branding), garageLocation, registration, availability.
- **Validation** (same style as `CombatDatabase.Validate`): missing/duplicate ids, stats out of range, missing prefab keys.
- Handling presets: Arcade, Street, Expert (scales assists, not raw stats).

## Driving features
Responsive steering, braking, handbrake drift, power slides, nitrous, damage plus repair, surface grip, third-person, hood
and cockpit cameras; controller and touch. `GameAction` already defines Accelerate/Brake/Steer/Handbrake/Nitrous with
default bindings.

## Garages
Southside Motors, East End Customs, Crown Luxury Motors, The Underground Lot, County Line Raceway. Starter capacity 1–2,
expanded by property. Purchases and sales go through `FinessBank` with idempotency keys.
