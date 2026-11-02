using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace HelperManager
{
    // The game's key bindings live in an input actions asset, not in code, and
    // players can rebind them. A plain-key binding also fires while Ctrl is
    // held (H is the horn, which is why the hotkey moved off Ctrl+H), so this
    // looks for any enabled game action on the hotkey's key.
    internal static class HotkeyConflictCheck
    {
        // Returns "Map/Action" for each enabled action bound to controlPath.
        public static List<string> FindBindingsOnKey(string controlPath)
        {
            var conflicts = new List<string>();
            try
            {
                foreach (InputAction action in InputSystem.ListEnabledActions())
                {
                    foreach (InputBinding binding in action.bindings)
                    {
                        if (string.Equals(binding.effectivePath, controlPath, StringComparison.OrdinalIgnoreCase))
                        {
                            conflicts.Add(action.actionMap?.name + "/" + action.name);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("Hotkey check failed: " + e.Message);
            }
            return conflicts;
        }
    }
}
