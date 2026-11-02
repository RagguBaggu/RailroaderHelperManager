using System.Collections.Generic;
using Game.Messages;
using Game.State;
using Model;
using UnityEngine;

namespace HelperManager
{
    // Used only if the Harmony patch couldn't be applied. Copies the lead's
    // throttle and reverser to each linked helper itself, with the same
    // formula vanilla's PeriodicUpdateForMu uses. Without the patch the
    // helper's own brakes don't work at all: SetAirGraph vents the brake
    // cylinder of a cut-out engine with no MU source. Auto Engineer also
    // doesn't count the helper's power. Only the patch fixes those.
    //
    // Host only, like vanilla: other players' writes would just echo.
    internal static class FallbackDriver
    {
        public static void Tick()
        {
            if (!StateManager.IsHost) return;
            foreach (KeyValuePair<BaseLocomotive, BaseLocomotive> pair in HelperLinks.ActiveSources)
            {
                BaseLocomotive helper = pair.Key;
                BaseLocomotive lead = pair.Value;
                if (helper == null || lead == null || lead.locomotiveControl == null) continue;
                // Extra lead units are coupled to the controller, so vanilla MU
                // already drives them; writing here too would fight it.
                if (HelperLinks.LinkedRole(helper) == HelperRole.Lead) continue;
                Drive(helper, lead);
            }
        }

        private static void Drive(BaseLocomotive helper, BaseLocomotive lead)
        {
            float throttle = lead.locomotiveControl.AbstractThrottle;
            float leadReverser = lead.locomotiveControl.AbstractReverser;

            // Unlike vanilla, a centred lead reverser centres the helper's too
            // (Mathf.Sign(0) is 1, so vanilla would drive forward).
            float reverser = 0f;
            if (Mathf.Abs(leadReverser) > 0.001f)
            {
                reverser = helper.CutoffSettingForVelocity(helper.velocity)
                    * (helper.FrontIsA == lead.FrontIsA ? 1f : -1f)
                    * Mathf.Sign(leadReverser);
                reverser = Mathf.CeilToInt(reverser * 20f) / 20f;
            }

            SendIfChanged(helper, PropertyChange.Control.Throttle, throttle);
            SendIfChanged(helper, PropertyChange.Control.Reverser, reverser);
        }

        private static void SendIfChanged(BaseLocomotive helper, PropertyChange.Control control, float value)
        {
            if (Mathf.Abs(helper.ControlProperties[control].FloatValue - value) > 0.001f)
            {
                helper.SendPropertyChange(control, value);
            }
        }
    }
}
