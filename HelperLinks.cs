using System.Collections.Generic;
using Game.Messages;
using Game.State;
using KeyValue.Runtime;
using Model;
using UI.Common;
using UnityEngine;

namespace HelperManager
{
    internal enum HelperRole
    {
        None,
        Lead,
        Mid,
        Rear,
    }

    // Helper links live on the helper cars themselves, as two custom keys in
    // each car's KeyValueObject. Written through PropertyChange, they only
    // need Crew access, replicate to every player, and are saved with the
    // car. The lead (controlling) engine carries no keys.
    //
    //   helperMgr.lead = lead car's id (car.id, not Ident)
    //   helperMgr.role = "lead" | "mid" | "rear"
    //
    // "lead" marks extra lead units coupled to the controlling engine; vanilla
    // MU already handles those, the key just records the player's choice.
    internal static class HelperLinks
    {
        private const string LeadKey = "helperMgr.lead";
        private const string RoleKey = "helperMgr.role";

        // helper -> lead, for every link that is currently valid. Read by the
        // Harmony prefix on every MU lookup (per engine, per physics step), so
        // it is rebuilt as a new dictionary and swapped in, never mutated.
        public static IReadOnlyDictionary<BaseLocomotive, BaseLocomotive> ActiveSources { get; private set; }
            = new Dictionary<BaseLocomotive, BaseLocomotive>();

        public static void Clear()
        {
            ActiveSources = new Dictionary<BaseLocomotive, BaseLocomotive>();
        }

        // ---- Reading ----

        public static string LinkedLeadId(Car car)
        {
            Value value = car.KeyValueObject[LeadKey];
            return value.IsNull ? null : value.StringValue;
        }

        public static HelperRole LinkedRole(Car car)
        {
            Value value = car.KeyValueObject[RoleKey];
            if (value.IsNull) return HelperRole.None;
            switch (value.StringValue)
            {
                case "lead": return HelperRole.Lead;
                case "mid": return HelperRole.Mid;
                case "rear": return HelperRole.Rear;
                default: return HelperRole.None;
            }
        }

        public static bool IsMu(Car car) => car.ControlProperties[PropertyChange.Control.Mu].BoolValue;

        public static bool IsCutOut(Car car) => car.ControlProperties[PropertyChange.Control.CutOut].BoolValue;

        // ---- Writing ----

        // Applies a validated plan. Order matters for the host's rescan: the
        // controller is cut in before helpers point at it, and each helper's
        // MU/cut-out is set before its link keys, so no intermediate state
        // looks like a broken link. ApplyLocal applies locally and sends in
        // order, so the host sees the same order.
        public static void Apply(LinkPlan plan)
        {
            BaseLocomotive controller = plan.Controller;
            if (IsMu(controller)) controller.ControlProperties[PropertyChange.Control.Mu] = false;
            if (IsCutOut(controller)) controller.ControlProperties[PropertyChange.Control.CutOut] = false;
            if (LinkedLeadId(controller) != null) ClearKeys(controller);

            foreach (BaseLocomotive engine in plan.Unlink)
            {
                Unlink(engine);
            }

            foreach (KeyValuePair<BaseLocomotive, HelperRole> pair in plan.Linked)
            {
                BaseLocomotive helper = pair.Key;
                if (!IsCutOut(helper)) helper.ControlProperties[PropertyChange.Control.CutOut] = true;
                if (!IsMu(helper)) helper.ControlProperties[PropertyChange.Control.Mu] = true;
                SetString(helper, LeadKey, controller.id);
                SetString(helper, RoleKey, RoleString(pair.Value));
            }

            Log.Info("Linked " + plan.Linked.Count + " engine(s) to " + controller.DisplayName);
        }

        // Removes every link pointing at this lead.
        public static int UnlinkAll(BaseLocomotive lead)
        {
            int count = 0;
            foreach (Car car in TrainController.Shared.Cars)
            {
                if (car is BaseLocomotive loco && LinkedLeadId(loco) == lead.id)
                {
                    Unlink(loco);
                    count++;
                }
            }
            Log.Info("Unlinked " + count + " engine(s) from " + lead.DisplayName);
            return count;
        }

        // Clears the keys, turns MU off and closes the throttle, so a helper
        // left behind doesn't keep pulling at its last setting. It stays cut
        // out: it's still in a train whose brakes the lead controls.
        public static void Unlink(BaseLocomotive helper)
        {
            ClearKeys(helper);
            if (IsMu(helper)) helper.ControlProperties[PropertyChange.Control.Mu] = false;
            helper.ControlProperties[PropertyChange.Control.Throttle] = 0f;
        }

        // Independent brake applied to a helper that drops off its train, so
        // it rolls to a gentle stop instead of coasting away.
        private const float DetachedBrake = 0.3f;

        // Brings a helper that has come off its train to a gentle stop, but
        // only when it's running light: nothing coupled except engines,
        // tenders and a caboose. Its brakes are vented while it's cut out with
        // no MU source, so it has to be cut in first. Cutting in puts its
        // automatic brake in release, which would release any cars still
        // coupled to it, so it isn't done when there are any.
        private static bool SettleIfRunningLight(BaseLocomotive helper)
        {
            foreach (Car car in helper.EnumerateCoupled())
            {
                if (!(car is BaseLocomotive) && !car.CarType.IsTender && !car.CarType.IsCaboose) return false;
            }
            if (IsCutOut(helper)) helper.ControlProperties[PropertyChange.Control.CutOut] = false;
            helper.ControlProperties[PropertyChange.Control.LocomotiveBrake] = DetachedBrake;
            return true;
        }

        private static void ClearKeys(Car car)
        {
            StateManager.ApplyLocal(new PropertyChange(car.id, LeadKey, new NullPropertyValue()));
            StateManager.ApplyLocal(new PropertyChange(car.id, RoleKey, new NullPropertyValue()));
        }

        private static void SetString(Car car, string key, string value)
        {
            StateManager.ApplyLocal(new PropertyChange(car.id, key, new StringPropertyValue(value)));
        }

        private static string RoleString(HelperRole role)
        {
            switch (role)
            {
                case HelperRole.Lead: return "lead";
                case HelperRole.Rear: return "rear";
                default: return "mid";
            }
        }

        // ---- Registry ----

        // Rebuilds ActiveSources from the keys on every car. Runs on every
        // machine (the patch is local). Only the host repairs broken links,
        // so players don't all send the same writes. The core holds repairs
        // off for a few seconds after a save loads, in case cars or their
        // properties are still being restored.
        public static void Rescan(bool allowRepair)
        {
            var active = new Dictionary<BaseLocomotive, BaseLocomotive>();
            var coupledToLead = new Dictionary<BaseLocomotive, HashSet<Car>>();
            List<(BaseLocomotive helper, string reason, bool separated)> broken = null;

            foreach (Car car in TrainController.Shared.Cars)
            {
                if (!(car is BaseLocomotive helper)) continue;
                string leadId = LinkedLeadId(helper);
                if (leadId == null) continue;

                string reason = CheckLink(helper, leadId, coupledToLead, out BaseLocomotive lead, out bool separated);
                if (reason == null)
                {
                    active[helper] = lead;
                }
                else
                {
                    (broken ??= new List<(BaseLocomotive, string, bool)>()).Add((helper, reason, separated));
                }
            }

            ActiveSources = active;

            if (broken == null || !allowRepair || !StateManager.IsHost) return;
            foreach ((BaseLocomotive helper, string reason, bool separated) in broken)
            {
                Unlink(helper);
                bool settled = separated && SettleIfRunningLight(helper);
                string what = reason + (settled ? ", braking to a stop" : "");
                Log.Info("Removing helper link on " + helper.DisplayName + ": " + what);
                Toast.Present("Helper Manager: " + helper.DisplayName + " unlinked (" + what + ").");
            }
        }

        // Null when the link is good; otherwise a short reason for the toast.
        // `separated` is true when the helper is no longer in a train with its
        // lead, so it may be rolling on its own.
        private static string CheckLink(BaseLocomotive helper, string leadId,
            Dictionary<BaseLocomotive, HashSet<Car>> coupledToLead, out BaseLocomotive lead, out bool separated)
        {
            lead = null;
            separated = true;
            if (!TrainController.Shared.TryGetCarForId(leadId, out Car leadCar) || !(leadCar is BaseLocomotive leadLoco))
            {
                return "its lead engine is gone";
            }
            lead = leadLoco;
            if (lead == helper) return "linked to itself";
            if (helper.set == null || helper.set != lead.set) return "uncoupled from " + lead.DisplayName;

            if (!coupledToLead.TryGetValue(lead, out HashSet<Car> coupled))
            {
                coupled = new HashSet<Car>(lead.EnumerateCoupled());
                coupledToLead[lead] = coupled;
            }
            if (!coupled.Contains(helper)) return "uncoupled from " + lead.DisplayName;

            separated = false;
            if (!IsMu(helper)) return "MU was turned off";
            if (IsMu(lead) || IsCutOut(lead)) return lead.DisplayName + " is no longer in control";
            return null;
        }
    }
}
