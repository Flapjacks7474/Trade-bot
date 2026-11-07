using System;
using System.Linq;
using FFTS.Economy;
using FFTS.Vehicles;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>Placeholder garage UI (IMGUI): buy, take out, repair, tow, sell, upgrades, garage space, handling preset.</summary>
    public sealed class GaragePanel : MonoBehaviour
    {
        private GarageDefinition _garage;
        private string _openId;
        private Vector2 _scroll;
        public bool IsOpen => _garage != null;

        private CoreGameManager Gm => CoreGameManager.Instance;

        public void Open(string garageId)
        {
            _garage = Gm.Session.Garage.Database.GetGarage(garageId);
            if (_garage == null) return;
            _openId = Guid.NewGuid().ToString("N").Substring(0, 12); // one purchase per model per visit: double clicks can't buy twice
            Gm.Lock(this);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        public void Close()
        {
            _garage = null;
            Gm.Unlock(this);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        private void OnGUI()
        {
            if (_garage == null) return;
            var g = Gm.Session.Garage; var db = g.Database; var bank = Gm.Session.Bank;
            var rect = new Rect(Screen.width / 2f - 330, 80, 660, Screen.height - 160);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 10, rect.width - 28, rect.height - 20));
            var label = DialoguePanel.RichLabel();
            GUILayout.Label($"<b><size=18>{_garage.DisplayName}</size></b>   Cash {FinessBank.Format(bank.Balance(FinessBank.Cash))} · Bank {FinessBank.Format(bank.Balance(FinessBank.Checking))}", label);
            if (g.OwnsGarage(_garage.Id)) GUILayout.Label($"Your space here: {g.Assigned(_garage.Id)}/{g.Capacity(_garage.Id)}", label);
            _scroll = GUILayout.BeginScrollView(_scroll);

            if (_garage.ForSale.Count > 0)
            {
                GUILayout.Label("<b>For sale</b>", label);
                foreach (var id in _garage.ForSale)
                {
                    var d = db.Get(id);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{d.Make} {d.DisplayName} · {d.Class} · {d.MaxSpeedKph:0} km/h · {FinessBank.Format(d.BaseValueCents)}", label);
                    if (GUILayout.Button("Buy", GUILayout.Width(80)))
                    {
                        var r = g.Buy(id, _garage.Id, _openId + "." + id, out var car);
                        Gm.Notify(r == VehicleResult.Ok ? $"Bought {d.DisplayName}. It's at {db.GetGarage(car.GarageId).DisplayName}." : "Can't buy: " + r);
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Your vehicles</b>", label);
            if (g.Vehicles.Count == 0) GUILayout.Label("None yet.", label);
            foreach (var v in g.Vehicles.ToArray())
            {
                var d = db.Get(v.DefinitionId);
                string where = v.Availability == VehicleAvailability.Stored ? "at " + db.GetGarage(v.GarageId).DisplayName : v.Availability.ToString().ToLowerInvariant();
                GUILayout.Label($"{d.DisplayName} — {where} · damage {v.Damage:0}% · {v.MileageKm:0.0} km", label);
                GUILayout.BeginHorizontal();
                bool here = v.GarageId == _garage.Id;
                if (v.Availability == VehicleAvailability.Stored && here && GUILayout.Button("Drive"))
                {
                    Close(); Gm.Driving.RetrieveAndDrive(v.OwnershipId); GUILayout.EndHorizontal(); break;
                }
                if (v.Damage > 0.01f && v.Availability != VehicleAvailability.Out && GUILayout.Button("Repair " + FinessBank.Format(g.RepairCost(v))))
                    Gm.Notify(g.Repair(v.OwnershipId, out var cost) == VehicleResult.Ok ? "Repaired for " + FinessBank.Format(cost) : "Can't afford the repair.");
                var driving = Gm.Driving.Current;
                string drivingId = driving != null ? driving.OwnershipId : null;
                if ((v.Availability == VehicleAvailability.Out || v.Availability == VehicleAvailability.Wrecked) && v.OwnershipId != drivingId
                    && GUILayout.Button("Tow here " + FinessBank.Format(GarageManager.RecoveryFeeCents)))
                {
                    var r = g.Recover(v.OwnershipId, _openId + ".tow." + v.OwnershipId);
                    if (r == VehicleResult.Ok) Gm.Driving.RespawnActiveVehicle();
                    Gm.Notify(r == VehicleResult.Ok ? "Towed." : "Tow failed: " + r);
                }
                if (v.Availability == VehicleAvailability.Stored && here && GUILayout.Button("Sell " + FinessBank.Format(g.ResaleValue(v))))
                    Gm.Notify(g.Sell(v.OwnershipId, out var price) == VehicleResult.Ok ? "Sold for " + FinessBank.Format(price) : "Can't sell now.");
                GUILayout.EndHorizontal();
                if (here && v.Availability == VehicleAvailability.Stored)
                {
                    GUILayout.BeginHorizontal();
                    foreach (var u in UpgradesFor(db, d))
                    {
                        bool has = v.Upgrades.Contains(u.Id);
                        GUI.enabled = !has;
                        if (GUILayout.Button((has ? "✔ " : "") + u.DisplayName + (has ? "" : " " + FinessBank.Format(u.PriceCents))))
                            Gm.Notify(g.InstallUpgrade(v.OwnershipId, u.Id) == VehicleResult.Ok ? u.DisplayName + " installed." : "Can't install.");
                        GUI.enabled = true;
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(8);
            if (g.OwnsGarage(_garage.Id) && g.Capacity(_garage.Id) < _garage.MaxCapacity && GUILayout.Button("Buy another space " + FinessBank.Format(_garage.SlotPriceCents)))
                Gm.Notify(g.BuyGarageSlot(_garage.Id) == VehicleResult.Ok ? "More room." : "Can't afford it.");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Handling: " + g.Preset, label);
            foreach (HandlingPreset p in Enum.GetValues(typeof(HandlingPreset)))
                if (GUILayout.Button(p.ToString())) g.Preset = p;
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            if (GUILayout.Button("Close", GUILayout.Height(30))) Close();
            GUILayout.EndArea();
        }

        private static System.Collections.Generic.IEnumerable<UpgradeDefinition> UpgradesFor(VehicleDatabase db, VehicleDefinition d)
        {
            foreach (var u in db.Upgrades) if (d.UpgradeSlots.Contains(u.Slot)) yield return u;
        }
    }
}
