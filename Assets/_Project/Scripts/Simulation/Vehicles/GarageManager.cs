using System;
using System.Collections.Generic;
using FFTS.Core;
using FFTS.Economy;

namespace FFTS.Vehicles
{
    public enum VehicleResult
    {
        Ok, UnknownVehicle, UnknownGarage, NotForSaleHere, GarageFull, InsufficientFunds, Duplicate, NotOwned,
        WrongState, GarageNotOwned, AtMaxCapacity, NothingToRepair
    }

    public sealed class VehiclePurchased : IGameEvent
    {
        public readonly string OwnershipId, DefinitionId; public readonly long PriceCents;
        public VehiclePurchased(string ownershipId, string definitionId, long price) { OwnershipId = ownershipId; DefinitionId = definitionId; PriceCents = price; }
    }

    public sealed class VehicleSold : IGameEvent
    {
        public readonly string OwnershipId; public readonly long PriceCents;
        public VehicleSold(string ownershipId, long price) { OwnershipId = ownershipId; PriceCents = price; }
    }

    public sealed class GarageState
    {
        public List<VehicleInstance> Vehicles = new List<VehicleInstance>();
        public List<string> OwnedGarages = new List<string>();
        public Dictionary<string, int> ExtraSlots = new Dictionary<string, int>();
        public string ActiveVehicle;
        public int NextOwnershipNumber = 1;
        public HandlingPreset Preset = HandlingPreset.Street;
    }

    /// <summary>
    /// Vehicle ownership: buy, sell, store, retrieve, repair, recover, upgrade, garage capacity. All money moves
    /// through <see cref="FinessBank"/> with idempotency keys supplied by the caller, so a double tap never
    /// buys two cars.
    /// </summary>
    public sealed class GarageManager
    {
        public const float ResaleFactor = 0.6f;
        public const long RecoveryFeeCents = 15000;

        private readonly VehicleDatabase _db;
        private readonly FinessBank _bank;
        private readonly EventBus _bus;
        private GarageState _s = new GarageState();
        private readonly HashSet<string> _usedKeys = new HashSet<string>();

        public VehicleDatabase Database => _db;
        public IReadOnlyList<VehicleInstance> Vehicles => _s.Vehicles;
        public string ActiveVehicle => _s.ActiveVehicle;
        public HandlingPreset Preset { get => _s.Preset; set => _s.Preset = value; }

        public GarageManager(VehicleDatabase db, FinessBank bank, EventBus bus)
        {
            _db = db; _bank = bank; _bus = bus;
            foreach (var g in db.Garages) if (g.StarterOwned) _s.OwnedGarages.Add(g.Id);
        }

        public VehicleInstance Get(string ownershipId) => _s.Vehicles.Find(v => v.OwnershipId == ownershipId);
        public bool OwnsGarage(string garageId) => _s.OwnedGarages.Contains(garageId);

        public int Capacity(string garageId)
        {
            var g = _db.GetGarage(garageId);
            if (g == null || !OwnsGarage(garageId)) return 0;
            _s.ExtraSlots.TryGetValue(garageId, out var extra);
            return Math.Min(g.MaxCapacity, g.BaseCapacity + extra);
        }

        /// <summary>Vehicles counted against a garage: stored there, or out but assigned to it.</summary>
        public int Assigned(string garageId) => _s.Vehicles.FindAll(v => v.GarageId == garageId).Count;

        public VehicleResult Buy(string definitionId, string garageId, string key, out VehicleInstance bought)
        {
            bought = null;
            var def = _db.Get(definitionId); var g = _db.GetGarage(garageId);
            if (def == null) return VehicleResult.UnknownVehicle;
            if (g == null) return VehicleResult.UnknownGarage;
            if (!g.ForSale.Contains(definitionId)) return VehicleResult.NotForSaleHere;
            // The bank's ledger is persisted, so it also catches a repeat after a reload.
            if (_bank.WasPosted("vehicle.buy." + key + ".cash") || _bank.WasPosted("vehicle.buy." + key + ".checking")) return VehicleResult.Duplicate;
            if (!_usedKeys.Add("buy." + key)) return VehicleResult.Duplicate;
            // Where does it live? The selling garage if owned and not full, else any owned garage with space.
            string home = OwnsGarage(garageId) && Assigned(garageId) < Capacity(garageId) ? garageId : FirstGarageWithSpace();
            if (home == null) { _usedKeys.Remove("buy." + key); return VehicleResult.GarageFull; }
            var paid = _bank.Pay("vehicle.buy." + key, def.BaseValueCents, TransactionKind.Purchase, "Bought " + def.DisplayName);
            if (paid != PostStatus.Posted) { _usedKeys.Remove("buy." + key); return paid == PostStatus.Duplicate ? VehicleResult.Duplicate : VehicleResult.InsufficientFunds; }
            bought = new VehicleInstance
            {
                OwnershipId = "veh." + _s.NextOwnershipNumber++, DefinitionId = definitionId, GarageId = home,
                Availability = VehicleAvailability.Stored
            };
            _s.Vehicles.Add(bought);
            _bus?.Publish(new VehiclePurchased(bought.OwnershipId, definitionId, def.BaseValueCents));
            return VehicleResult.Ok;
        }

        private string FirstGarageWithSpace()
        {
            foreach (var id in _s.OwnedGarages) if (Assigned(id) < Capacity(id)) return id;
            return null;
        }

        public long ResaleValue(VehicleInstance v)
        {
            var d = _db.Get(v.DefinitionId);
            float rarity = 1f + (int)d.Rarity * 0.1f;
            float wear = Math.Max(0.4f, 1f - v.MileageKm / 200000f);
            float condition = 1f - v.Damage / 150f;
            long upgrades = 0;
            foreach (var u in v.Upgrades) upgrades += _db.GetUpgrade(u)?.PriceCents ?? 0;
            return (long)((d.BaseValueCents * rarity * wear * condition + upgrades * 0.4f) * ResaleFactor);
        }

        public VehicleResult Sell(string ownershipId, out long price)
        {
            price = 0;
            var v = Get(ownershipId);
            if (v == null) return VehicleResult.NotOwned;
            if (v.Availability == VehicleAvailability.Impounded || _s.ActiveVehicle == ownershipId) return VehicleResult.WrongState;
            price = Math.Max(1, ResaleValue(v));
            _bank.Post(new TransactionRequest { IdempotencyKey = "vehicle.sell." + ownershipId, Kind = TransactionKind.Sale, To = FinessBank.Cash, AmountCents = price, Memo = "Sold " + _db.Get(v.DefinitionId).DisplayName });
            _s.Vehicles.Remove(v);
            _bus?.Publish(new VehicleSold(ownershipId, price));
            return VehicleResult.Ok;
        }

        /// <summary>Takes a stored vehicle out at its garage and makes it the active vehicle.</summary>
        public VehicleResult Retrieve(string ownershipId, out GarageDefinition from)
        {
            from = null;
            var v = Get(ownershipId);
            if (v == null) return VehicleResult.NotOwned;
            if (v.Availability != VehicleAvailability.Stored) return VehicleResult.WrongState;
            if (_s.ActiveVehicle != null && _s.ActiveVehicle != ownershipId) StoreOrLeave(_s.ActiveVehicle);
            from = _db.GetGarage(v.GarageId);
            v.Availability = VehicleAvailability.Out;
            v.WorldX = from.SpawnX; v.WorldZ = from.SpawnZ; v.HeadingDeg = from.SpawnHeadingDeg;
            _s.ActiveVehicle = ownershipId;
            return VehicleResult.Ok;
        }

        private void StoreOrLeave(string ownershipId) { /* previous active vehicle stays parked where it is */ _s.ActiveVehicle = null; }

        /// <summary>Parks a vehicle that is out into an owned garage with room (normally the one the player is at).</summary>
        public VehicleResult Store(string ownershipId, string garageId)
        {
            var v = Get(ownershipId);
            if (v == null) return VehicleResult.NotOwned;
            if (v.Availability != VehicleAvailability.Out) return VehicleResult.WrongState;
            if (_db.GetGarage(garageId) == null) return VehicleResult.UnknownGarage;
            if (!OwnsGarage(garageId)) return VehicleResult.GarageNotOwned;
            if (v.GarageId != garageId && Assigned(garageId) >= Capacity(garageId)) return VehicleResult.GarageFull;
            v.GarageId = garageId;
            v.Availability = VehicleAvailability.Stored;
            if (_s.ActiveVehicle == ownershipId) _s.ActiveVehicle = null;
            return VehicleResult.Ok;
        }

        /// <summary>Writes back a driving session's results (position, damage, mileage).</summary>
        public void RecordDrive(string ownershipId, VehicleDynamics d, double addedMeters)
        {
            var v = Get(ownershipId);
            if (v == null) return;
            v.WorldX = d.X; v.WorldZ = d.Z; v.HeadingDeg = d.HeadingDeg;
            v.Damage = d.Damage;
            v.MileageKm += (float)(addedMeters / 1000.0);
            if (d.IsWrecked) v.Availability = VehicleAvailability.Wrecked;
        }

        public long RepairCost(VehicleInstance v)
        {
            var d = _db.Get(v.DefinitionId);
            if (v.Damage <= 0.01f) return 0;
            return 2500 + (long)(d.BaseValueCents * 0.004f * v.Damage);
        }

        public VehicleResult Repair(string ownershipId, out long cost)
        {
            cost = 0;
            var v = Get(ownershipId);
            if (v == null) return VehicleResult.NotOwned;
            if (v.Damage <= 0.01f) return VehicleResult.NothingToRepair;
            if (v.Availability == VehicleAvailability.Impounded) return VehicleResult.WrongState;
            cost = RepairCost(v);
            var paid = _bank.Pay($"vehicle.repair.{ownershipId}.{v.RepairCount}", cost, TransactionKind.Purchase, "Repair " + _db.Get(v.DefinitionId).DisplayName);
            if (paid != PostStatus.Posted) return VehicleResult.InsufficientFunds;
            v.Damage = 0; v.RepairCount++;
            if (v.Availability == VehicleAvailability.Wrecked) v.Availability = VehicleAvailability.Stored;
            return VehicleResult.Ok;
        }

        /// <summary>Tows an abandoned (out) or wrecked vehicle back to its garage for a fee.</summary>
        public VehicleResult Recover(string ownershipId, string key)
        {
            var v = Get(ownershipId);
            if (v == null) return VehicleResult.NotOwned;
            if (v.Availability != VehicleAvailability.Out && v.Availability != VehicleAvailability.Wrecked) return VehicleResult.WrongState;
            var paid = _bank.Pay("vehicle.recover." + key, RecoveryFeeCents, TransactionKind.Bill, "Tow to garage");
            if (paid == PostStatus.Duplicate) return VehicleResult.Duplicate;
            if (paid != PostStatus.Posted) return VehicleResult.InsufficientFunds;
            if (v.Availability == VehicleAvailability.Out) v.Availability = VehicleAvailability.Stored; // wrecked stays wrecked until repaired
            if (_s.ActiveVehicle == ownershipId) _s.ActiveVehicle = null;
            return VehicleResult.Ok;
        }

        public VehicleResult BuyGarageSlot(string garageId)
        {
            var g = _db.GetGarage(garageId);
            if (g == null) return VehicleResult.UnknownGarage;
            if (!OwnsGarage(garageId)) return VehicleResult.GarageNotOwned;
            if (Capacity(garageId) >= g.MaxCapacity) return VehicleResult.AtMaxCapacity;
            _s.ExtraSlots.TryGetValue(garageId, out var n);
            var paid = _bank.Pay($"garage.slot.{garageId}.{n + 1}", g.SlotPriceCents, TransactionKind.Purchase, "Garage space " + g.DisplayName);
            if (paid != PostStatus.Posted) return VehicleResult.InsufficientFunds;
            _s.ExtraSlots[garageId] = n + 1;
            return VehicleResult.Ok;
        }

        public VehicleResult InstallUpgrade(string ownershipId, string upgradeId)
        {
            var v = Get(ownershipId); var u = _db.GetUpgrade(upgradeId);
            if (v == null) return VehicleResult.NotOwned;
            if (u == null) return VehicleResult.UnknownVehicle;
            var def = _db.Get(v.DefinitionId);
            if (!def.UpgradeSlots.Contains(u.Slot)) return VehicleResult.WrongState;
            if (v.Upgrades.Contains(upgradeId)) return VehicleResult.Duplicate;
            var paid = _bank.Pay($"vehicle.upgrade.{ownershipId}.{upgradeId}", u.PriceCents, TransactionKind.Purchase, u.DisplayName);
            if (paid != PostStatus.Posted) return VehicleResult.InsufficientFunds;
            v.Upgrades.RemoveAll(x => _db.GetUpgrade(x)?.Slot == u.Slot); // one per slot
            v.Upgrades.Add(upgradeId);
            return VehicleResult.Ok;
        }

        public VehicleDynamics CreateDynamics(string ownershipId)
        {
            var v = Get(ownershipId) ?? throw new ArgumentException("Not owned: " + ownershipId);
            return new VehicleDynamics(_db.StatsFor(v), _s.Preset, v.Damage) { X = v.WorldX, Z = v.WorldZ, HeadingDeg = v.HeadingDeg };
        }

        public GarageState Capture()
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(_s);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<GarageState>(json);
            return copy;
        }

        public void Restore(GarageState s)
        {
            _s = s ?? new GarageState();
            foreach (var g in _db.Garages) if (g.StarterOwned && !_s.OwnedGarages.Contains(g.Id)) _s.OwnedGarages.Add(g.Id);
            _s.Vehicles.RemoveAll(v => _db.Get(v.DefinitionId) == null); // content removed: drop rather than crash
            _usedKeys.Clear();
        }
    }
}
