using System;
using FFTS.Input;
using FFTS.Vehicles;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>
    /// Free driving: spawns the player's active vehicle, enter/exit, fixed-step simulation at 60 Hz, collisions,
    /// storing at owned garages, wrecks, and handing over to <see cref="RaceDirector"/> at race starts.
    /// </summary>
    public sealed class DrivingDirector : MonoBehaviour
    {
        public const float ExitMaxSpeed = 2f;
        public const float GarageRadius = 22f;

        public VehicleView Current { get; private set; }   // the car the player is in
        public VehicleView Parked { get; private set; }    // the active car, spawned in the world but empty
        public bool IsDriving => Current != null;
        public string Prompt { get; private set; }
        public int CameraView { get; private set; }

        private float _accumulator;
        private double _recordedMeters;

        private CoreGameManager Gm => CoreGameManager.Instance;

        private void Start()
        {
            Gm.Reloaded += RespawnActiveVehicle;
            RespawnActiveVehicle();
        }

        /// <summary>Spawns the saved active vehicle (if it is out) parked where it was left.</summary>
        public void RespawnActiveVehicle()
        {
            if (IsDriving) ForceExit();
            DespawnParked();
            var g = Gm.Session.Garage;
            var id = g.ActiveVehicle;
            var inst = id != null ? g.Get(id) : null;
            if (inst == null || inst.Availability != VehicleAvailability.Out) return;
            SpawnParked(inst);
        }

        private void SpawnParked(VehicleInstance inst)
        {
            var g = Gm.Session.Garage;
            var dyn = g.CreateDynamics(inst.OwnershipId);
            var def = g.Database.Get(inst.DefinitionId);
            Parked = VehicleView.Spawn(def.DisplayName, dyn, inst.OwnershipId, inst.Customization.PaintColor);
            var w = Parked.gameObject.AddComponent<WorldInteractable>();
            w.Id = "drive." + inst.OwnershipId; w.Verb = "drive"; w.Prompt = "Drive " + def.DisplayName; w.Radius = 3.5f; w.Priority = 1;
            var view = Parked;
            w.OnInteract = () => Enter(view);
            _recordedMeters = dyn.DistanceMeters;
        }

        private void DespawnParked()
        {
            if (Parked != null) Destroy(Parked.gameObject);
            Parked = null;
        }

        /// <summary>Takes a stored car out of a garage and puts the player in it.</summary>
        public void RetrieveAndDrive(string ownershipId)
        {
            var g = Gm.Session.Garage;
            if (IsDriving) { Gm.Notify("Get out of your current car first."); return; }
            if (g.ActiveVehicle != null && g.ActiveVehicle != ownershipId && Parked != null)
            {
                g.RecordDrive(Parked.OwnershipId, Parked.Dyn, 0); // leave it where it is
                DespawnParked();
            }
            var r = g.Retrieve(ownershipId, out _);
            if (r != VehicleResult.Ok) { Gm.Notify("Can't take that car out: " + r); return; }
            SpawnParked(g.Get(ownershipId));
            Enter(Parked);
        }

        public void Enter(VehicleView view)
        {
            if (view == null || IsDriving) return;
            if (view.Dyn.IsWrecked) { Gm.Notify("It's wrecked. Get it towed and repaired at a garage."); return; }
            Current = view; Parked = null;
            var interact = view.GetComponent<WorldInteractable>();
            if (interact != null) interact.enabled = false;
            Gm.Lock(this);
            SetPlayerPresent(false);
            _accumulator = 0;
            _recordedMeters = view.Dyn.DistanceMeters;
            Gm.Notify("Driving " + view.DisplayName + "  ·  E to get out · C camera · Space handbrake · Shift nitrous");
        }

        public void Exit()
        {
            if (!IsDriving) return;
            Flush();
            var car = Current;
            Current = null; Parked = car;
            var interact = car.GetComponent<WorldInteractable>();
            if (interact != null) interact.enabled = true;
            var t = car.transform;
            Vector3 spot = t.position - t.right * 2.4f;
            if (Physics.CheckCapsule(spot + Vector3.up * 0.5f, spot + Vector3.up * 1.5f, 0.4f, ~0, QueryTriggerInteraction.Ignore)) spot = t.position + t.right * 2.4f;
            var player = Gm.Player;
            player.SetPositionAndRotation(spot + Vector3.up * 0.1f, Quaternion.Euler(0, t.eulerAngles.y, 0));
            SetPlayerPresent(true);
            Gm.Unlock(this);
            CameraRigOrNull()?.SetExplore();
        }

        private void ForceExit()
        {
            if (!IsDriving) return;
            Exit();
        }

        private void SetPlayerPresent(bool present)
        {
            var p = Gm.Player;
            if (p == null) return;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = present;
            foreach (var r in p.GetComponentsInChildren<Renderer>()) r.enabled = present;
        }

        /// <summary>Writes the current car's position/damage/mileage into the garage state (before saving).</summary>
        public void Flush()
        {
            var car = Current != null ? Current : Parked;
            if (car == null) return;
            double added = car.Dyn.DistanceMeters - _recordedMeters;
            _recordedMeters = car.Dyn.DistanceMeters;
            Gm.Session.Garage.RecordDrive(car.OwnershipId, car.Dyn, Math.Max(0, added));
        }

        private void Update()
        {
            Prompt = null;
            if (!IsDriving) return;
            var race = Gm.Races;
            var frame = Gm.Frame;
            var rig = CameraRigOrNull();
            if (frame.WasPressed(ButtonMask.CameraView)) CameraView = (CameraView + 1) % 3;
            rig?.SetVehicle(Current.transform, CameraView);
            Gm.Player.position = Current.transform.position + Vector3.up * 0.1f; // keeps location triggers and saves working

            if (race.Active) return; // the race director steps every car during a race

            _accumulator += Mathf.Min(Time.deltaTime, 0.1f);
            const float dt = 1f / 60f;
            var input = DriveInputMapper.Map(frame, Gm.Layout.SurfaceGripAt(Current.Dyn.X, Current.Dyn.Z));
            while (_accumulator >= dt)
            {
                _accumulator -= dt;
                Current.Dyn.Step(input, dt);
                Current.SyncFromSim(closing =>
                {
                    float dmg = Current.Dyn.ApplyImpact(closing);
                    if (dmg > 8f) Gm.Notify($"Crash! Damage {Current.Dyn.Damage:0}%");
                });
            }

            if (Current.Dyn.IsWrecked)
            {
                Gm.Notify("Your car is wrecked. Tow it from a garage, then repair it.");
                Flush();
                Exit();
                return;
            }

            // Context action when (nearly) stopped.
            bool slow = Mathf.Abs(Current.Dyn.Speed) < ExitMaxSpeed;
            string garage = OwnedGarageNearby();
            var raceDef = race.RaceStartNearby(Current.transform.position);
            if (raceDef != null) Prompt = $"[E] Start {raceDef.DisplayName} (entry {FFTS.Economy.FinessBank.Format(raceDef.EntryFeeCents)})";
            else if (garage != null) Prompt = "[E] Park in " + Gm.Session.Garage.Database.GetGarage(garage).DisplayName;
            else Prompt = slow ? "[E] Get out" : null;

            if (slow && frame.WasPressed(ButtonMask.Interact))
            {
                if (raceDef != null) race.TryStart(raceDef, Current);
                else if (garage != null) StoreAt(garage);
                else Exit();
            }
        }

        private string OwnedGarageNearby()
        {
            var g = Gm.Session.Garage;
            foreach (var def in g.Database.Garages)
            {
                if (!g.OwnsGarage(def.Id)) continue;
                if (Vector2.Distance(new Vector2(def.SpawnX, def.SpawnZ), new Vector2(Current.Dyn.X, Current.Dyn.Z)) <= GarageRadius) return def.Id;
            }
            return null;
        }

        private void StoreAt(string garageId)
        {
            Flush();
            var car = Current;
            var r = Gm.Session.Garage.Store(car.OwnershipId, garageId);
            if (r != VehicleResult.Ok) { Gm.Notify("Can't park here: " + r); return; }
            Exit();
            DespawnParked();
            Gm.Notify(car.DisplayName + " parked.");
        }

        internal static CameraRig CameraRigOrNull()
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var rig = cam.GetComponent<CameraRig>();
            return rig != null ? rig : null;
        }
    }
}
