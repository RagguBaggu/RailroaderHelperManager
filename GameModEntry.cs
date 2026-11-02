using Railroader.Modding;
using UnityEngine;

namespace HelperManager
{
    // Entry point for Railroader's built-in mod loader (2026.1+). The game
    // loads the DLLs named in Info.json and instantiates every public
    // IModEntryPoint it finds. DLL mods only run when Preferences >
    // Experimental > "Allow Unsafe DLL Mods" is on.
    //
    // Mods can be unloaded and reloaded mid-session (e.g. when the active mod
    // set changes to match a save): OnModLoad is called on a fresh instance
    // immediately after OnModUnload on the old one.
    //
    // This class must not reference any Harmony type (see HarmonyResolver).
    public sealed class GameModEntry : IModEntryPoint, IWorldLifecycle
    {
        private GameObject _hostObject;
        private HelperManagerCore _core;

        public void OnModLoad(IModContext context)
        {
            IModLogger logger = context.Log;
            Log.InfoHandler = logger.Info;
            Log.WarningHandler = logger.Warning;
            Log.ErrorHandler = logger.Error;

            HarmonyResolver.Install(context.ModDirectory);

            _hostObject = new GameObject("HelperManager");
            Object.DontDestroyOnLoad(_hostObject);
            _core = _hostObject.AddComponent<HelperManagerCore>();
        }

        public void OnModUnload()
        {
            if (_hostObject != null)
            {
                // DestroyImmediate so a reload's fresh instance never overlaps
                // this one (Destroy is deferred to the end of the frame).
                Object.DestroyImmediate(_hostObject);
            }
            _hostObject = null;
            _core = null;
        }

        public void OnWorldLoad()
        {
            if (_core != null) _core.OnWorldLoaded();
        }

        public void OnWorldUnload()
        {
            if (_core != null) _core.OnWorldUnloaded();
        }
    }
}
