using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HelperManager
{
    // The game doesn't ship Harmony, and its mod loader only Assembly.LoadFroms
    // the DLLs listed in Info.json (each of which must contain an
    // IModEntryPoint), with no AssemblyResolve handler of its own. So this
    // mod ships 0Harmony.dll unlisted in its own folder and resolves it here.
    //
    // Nothing in this file touches a Harmony type: it must run before the
    // runtime first tries to bind 0Harmony. Code that uses Harmony lives in
    // separate classes that are only called after Install().
    internal static class HarmonyResolver
    {
        private const string HarmonyAssemblyName = "0Harmony";

        // Static state outlives a mod unload/reload: the loader reuses the
        // already-loaded HelperManager assembly, so the handler is only ever
        // added once per process.
        private static bool _installed;
        private static string _modDirectory;

        public static void Install(string modDirectory)
        {
            _modDirectory = modDirectory;
            if (_installed) return;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            _installed = true;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (!string.Equals(new AssemblyName(args.Name).Name, HarmonyAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Another mod may already have loaded a Harmony. Harmony 2.x isn't
            // strong-named and shares patch state between copies, so reuse it
            // rather than loading a second copy.
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loaded.GetName().Name == HarmonyAssemblyName)
                {
                    Log.Info("Resolver: using already-loaded Harmony " + loaded.GetName().Version);
                    return loaded;
                }
            }

            string path = Path.Combine(_modDirectory ?? "", HarmonyAssemblyName + ".dll");
            if (!File.Exists(path))
            {
                Log.Error("Resolver: bundled Harmony not found at " + path);
                return null;
            }
            Log.Info("Resolver: loading bundled Harmony from " + path);
            return Assembly.LoadFrom(path);
        }
    }

    // The first (and so far only) code that binds 0Harmony. Kept out of line
    // so a missing Harmony throws from this call, inside the caller's
    // try/catch, rather than when the caller itself is compiled.
    internal static class HarmonyStatus
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Describe()
        {
            Assembly assembly = typeof(HarmonyLib.Harmony).Assembly;
            return "Harmony " + assembly.GetName().Version + " (" + assembly.Location + ")";
        }
    }
}
