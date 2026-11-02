using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;

namespace HelperManager
{
    // The one game patch. Vanilla MU looks for its source engine with
    // BaseLocomotive.FindSourceLocomotive(LogicalEnd), which walks the
    // consist and gives up at the first car that isn't an engine or tender,
    // so helpers separated from the lead by cars never find it. For engines
    // this mod linked, the prefix returns the lead straight away. Everything
    // downstream (throttle/reverser following, independent brake deferral,
    // Auto Engineer's tractive effort and braking model) then treats the
    // helper as a normal MU unit.
    //
    // FindSourceLocomotive is patched rather than its caller
    // FindMuSourceLocomotive because it contains a loop and a throw, so
    // Mono's JIT can't inline it past the patch.
    //
    // Fields here are typed object, not Harmony types, so the class itself
    // loads without 0Harmony; only the method bodies bind it.
    internal static class MuSourcePatch
    {
        private const string HarmonyId = "raggubaggu.helper-manager";

        private static object _harmony;
        private static MethodBase _target;

        public static bool IsActive { get; private set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Apply()
        {
            if (IsActive) return;
            _target = AccessTools.Method(typeof(BaseLocomotive), "FindSourceLocomotive", new[] { typeof(Car.LogicalEnd) });
            if (_target == null)
            {
                throw new MissingMethodException("BaseLocomotive.FindSourceLocomotive(Car.LogicalEnd) not found; the game may have changed.");
            }
            var harmony = new Harmony(HarmonyId);
            harmony.Patch(_target, prefix: new HarmonyMethod(typeof(MuSourcePatch), nameof(Prefix)));
            _harmony = harmony;
            IsActive = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Remove()
        {
            if (!IsActive) return;
            ((Harmony)_harmony).Unpatch(_target, HarmonyPatchType.Prefix, HarmonyId);
            IsActive = false;
        }

        // Called for every MU lookup: per engine, per physics step. One
        // dictionary lookup on the mod's precomputed table of valid links.
        private static bool Prefix(BaseLocomotive __instance, ref BaseLocomotive __result)
        {
            if (HelperLinks.ActiveSources.TryGetValue(__instance, out BaseLocomotive lead) && lead != null)
            {
                __result = lead;
                return false;
            }
            return true;
        }
    }
}
