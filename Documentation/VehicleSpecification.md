# Vehicle Specification — status: IMPLEMENTED (simulation tested; Unity layer unverified)

## Implemented (Milestone 4)
| Piece | Where | Notes |
|---|---|---|
| `VehicleDefinition` / `UpgradeDefinition` / `GarageDefinition` | `Simulation/Vehicles/VehicleData.cs`, `Data/Vehicles/vehicles.json` | Validation: duplicate ids, stat ranges, missing prefab keys, unknown sale vehicles |
| `VehicleInstance` (saved) | same | Damage, mileage, upgrades, customization, garage, availability (Stored/Out/Wrecked/Impounded), last position |
| `VehicleDynamics` | `Simulation/Vehicles/VehicleDynamics.cs` | Deterministic 60 Hz model: launch traction, accel falloff, drag, braking, reverse, speed-scaled steering, drift angle, handbrake, surface grip, nitrous (needs the kit), impact damage, wreck at 100% |
| Handling presets | Arcade / Street / Expert | Change grip, steering and drift recovery assists; never top speed |
| `GarageManager` | `Simulation/Vehicles/GarageManager.cs` | Buy (idempotent, routed to an owned garage with space), sell (resale from rarity/mileage/damage/upgrades), retrieve, store, repair, tow ($150), upgrades (one per slot), extra garage space |
| Unity | `Runtime/Driving/*` | Kinematic box-swept collision, enter/exit (E under 2 m/s), chase/hood/cockpit (C), park at owned garage, garage panel |

## Content
| Vehicle | Make (fictional) | Class | Price | Notes |
|---|---|---|---|---|
| Magnolia Cruiser '86 | Delmont | Muscle, RWD | $750 | starter; tough, wide turning |
| Kudzu RS | Hoshiro | Tuner, FWD | $2,400 | sharp turn-in, fragile |
| Crestmore Eight | Crestmore | Luxury sedan, AWD | $9,500 | not sold in Southside yet; highest top speed |

Garages: **Grandma's Driveway** (owned from the start, 1 space, upgradable to 2) and **Southside Motors** (dealer selling the
Cruiser and the Kudzu). Upgrades: Engine Stage 1, Street Brakes, Street Tires, Nitrous Kit.

## Original plan (kept for reference)

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
