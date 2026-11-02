using System;
using System.Collections.Generic;
using UI;
using UI.Common;
using UI.Windows;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HelperManager
{
    // All runtime behavior. A plain MonoBehaviour on the mod's own
    // DontDestroyOnLoad object, so it isn't tied to the loader. It's
    // destroyed (DestroyImmediate) when the mod unloads, which removes the
    // patch.
    public sealed class HelperManagerCore : MonoBehaviour
    {
        // How often links are revalidated and the open window checks the
        // consist for changes.
        private const float RescanInterval = 0.5f;

        // How often the fallback loop copies controls (patch unavailable only).
        private const float FallbackInterval = 0.25f;

        // No link repairs this soon after a save loads, while cars may still
        // be restoring.
        private const float RepairGraceSeconds = 5f;

        // Ctrl+5. The 5 key is unbound in every game action map (checked
        // against the game's input asset), so it has no side effects.
        private const string HotkeyName = "Ctrl+5";
        private const string HotkeyControlPath = "<Keyboard>/5";

        private bool _worldLoaded;
        private float _worldLoadedAt;
        private float _nextRescan;
        private float _nextFallbackTick;
        private string _lastSignature;
        private float _lastErrorLogTime = -100f;

        private void Start()
        {
            try
            {
                Log.Info("Loaded. " + HarmonyStatus.Describe());
            }
            catch (Exception e)
            {
                HelperWindow.HarmonyError = e.Message;
                Log.Error("Harmony could not be loaded: " + e);
                return;
            }

            try
            {
                MuSourcePatch.Apply();
                Log.Info("MU source patch applied.");
            }
            catch (Exception e)
            {
                Log.Error("MU source patch failed; using fallback mode: " + e);
            }
        }

        private void OnDestroy()
        {
            HelperLinks.Clear();
            try
            {
                if (MuSourcePatch.IsActive)
                {
                    MuSourcePatch.Remove();
                    Log.Info("MU source patch removed.");
                }
            }
            catch (Exception e)
            {
                Log.Error("Removing the MU source patch failed: " + e);
            }
        }

        public void OnWorldLoaded()
        {
            _worldLoaded = true;
            _worldLoadedAt = Time.unscaledTime;
            _lastSignature = null;
            HelperLinks.Clear();
            HelperWindow.ResetDraft();
            WarnIfHotkeyConflicts();
        }

        // Drops every reference into the old world's cars.
        public void OnWorldUnloaded()
        {
            _worldLoaded = false;
            _lastSignature = null;
            HelperLinks.Clear();
            HelperWindow.ResetDraft();
        }

        private static void WarnIfHotkeyConflicts()
        {
            List<string> conflicts = HotkeyConflictCheck.FindBindingsOnKey(HotkeyControlPath);
            if (conflicts.Count == 0) return;
            string actions = string.Join(", ", conflicts);
            Log.Warning("Hotkey " + HotkeyName + " also triggers game action(s): " + actions);
            Toast.Present("Helper Manager: " + HotkeyName + " also triggers " + actions + ". Rebind it in the game's controls.");
        }

        // An uncaught exception in Update can stop Unity calling it at all,
        // so everything runs inside one top-level catch.
        private void Update()
        {
            try
            {
                UpdateInternal();
            }
            catch (Exception e)
            {
                if (Time.unscaledTime - _lastErrorLogTime > 5f)
                {
                    _lastErrorLogTime = Time.unscaledTime;
                    Log.Error("Update failed: " + e);
                }
            }
        }

        private void UpdateInternal()
        {
            if (!_worldLoaded || WindowManager.Shared == null || TrainController.Shared == null) return;

            if (HotkeyPressed())
            {
                _lastSignature = null;
                HelperWindow.Toggle();
            }

            float now = Time.unscaledTime;
            if (now >= _nextRescan)
            {
                _nextRescan = now + RescanInterval;
                HelperLinks.Rescan(allowRepair: now - _worldLoadedAt > RepairGraceSeconds);
                RefreshWindowIfChanged();
            }

            if (!MuSourcePatch.IsActive && now >= _nextFallbackTick)
            {
                _nextFallbackTick = now + FallbackInterval;
                FallbackDriver.Tick();
            }
        }

        // Ignored while the player is typing in a text field (GameInput
        // switches to UI input mode then).
        private static bool HotkeyPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.digit5Key.wasPressedThisFrame) return false;
            if (!keyboard.ctrlKey.isPressed || keyboard.shiftKey.isPressed || keyboard.altKey.isPressed) return false;
            return GameInput.MovementInputEnabled;
        }

        // Selection changes rebuild the window through the game's own
        // SelectedCarChanged event; coupling, MU, cut-out, air and link
        // changes have no event, so poll a fingerprint and rebuild only when
        // it differs.
        private void RefreshWindowIfChanged()
        {
            if (!HelperWindow.IsOpen)
            {
                _lastSignature = null;
                return;
            }
            string signature = ConsistReader.Signature(TrainController.Shared.SelectedCar);
            if (signature == _lastSignature) return;
            bool firstCheck = _lastSignature == null;
            _lastSignature = signature;
            if (!firstCheck) HelperWindow.SetNeedsRebuild();
        }
    }
}
