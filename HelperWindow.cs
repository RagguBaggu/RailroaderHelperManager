using System.Collections.Generic;
using System.Linq;
using Game.Events;
using Model;
using UI.Builder;
using UI.Common;
using UI.Windows;
using UnityEngine;

namespace HelperManager
{
    // The in-game Helper Manager window, built on the game's own custom
    // window API (the same one its LoadInventoryCommand uses). The game
    // destroys custom windows and their definitions when returning to the
    // main menu, so the definition is registered on demand.
    internal static class HelperWindow
    {
        private const string WindowId = "raggubaggu.helper-manager";
        private const string Title = "Helper Manager";

        private const string DimColor = "#9A9A9A";
        private const string MuColor = "#7FB8E0";
        private const string WarnColor = "#E0A040";
        private const string ErrorColor = "#E06060";
        private const string SelectedColor = "#E8C872";

        // Dropdown order matches HelperRole.
        private static readonly List<string> RoleNames = new List<string> { "—", "Lead", "Mid", "Rear" };

        // Set by the core when Harmony fails to load; shown as a warning.
        public static string HarmonyError;

        // The player's role choices (car id -> role), kept across rebuilds
        // until the consist or its links change underneath them.
        private static Dictionary<string, HelperRole> _draft = new Dictionary<string, HelperRole>();
        private static string _draftKey;

        public static bool IsOpen
        {
            get
            {
                if (WindowManager.Shared == null || !WindowManager.Shared.HasDefinition(WindowId)) return false;
                CustomWindowHandle handle = CustomWindowHandle.Create(WindowId);
                return handle != null && handle.IsOpen();
            }
        }

        public static void Toggle()
        {
            if (!EnsureRegistered()) return;
            CustomWindowHandle handle = CustomWindowHandle.Create(WindowId);
            if (handle != null) handle.Toggle();
        }

        public static void SetNeedsRebuild()
        {
            if (WindowManager.Shared == null || !WindowManager.Shared.HasDefinition(WindowId)) return;
            CustomWindowHandle handle = CustomWindowHandle.Create(WindowId);
            if (handle != null) handle.SetNeedsRebuild();
        }

        public static void ResetDraft()
        {
            _draftKey = null;
        }

        private static bool EnsureRegistered()
        {
            WindowManager manager = WindowManager.Shared;
            if (manager == null) return false;
            if (manager.HasDefinition(WindowId)) return true;

            manager.RegisterCustomWindow(new CustomWindowDefinition
            {
                Identifier = WindowId,
                Title = Title,
                Config = WindowConfig.Resizable(new Vector2Int(640, 680), new Vector2Int(480, 400), Window.Position.CenterRight),
                BuilderAction = Build,
            });
            return true;
        }

        private static void Build(UIPanelBuilder builder)
        {
            builder.RebuildOnEvent<SelectedCarChanged>();

            if (HarmonyError != null)
            {
                builder.AddLabel("<color=" + WarnColor + ">Harmony failed to load: " + HarmonyError + "</color>");
            }
            else if (!MuSourcePatch.IsActive)
            {
                builder.AddLabel("<color=" + WarnColor + ">Fallback mode: helpers follow throttle and reverser only. "
                    + "Their own brakes won't apply, and Auto Engineer won't count their power.</color>");
            }

            TrainController trainController = TrainController.Shared;
            Car selected = trainController == null ? null : trainController.SelectedCar;
            if (selected == null || !(selected is BaseLocomotive))
            {
                builder.AddLabelEmptyState("Select a locomotive to see the engines in its consist.");
                return;
            }

            ConsistInfo consist = ConsistReader.Read(selected);
            SyncDraft(consist);
            LinkPlan plan = LinkPlanner.Plan(consist, _draft);
            bool hasLinks = consist.Engines.Any(e => e.LinkedLeadId != null);

            builder.AddLabel("<size=120%><b>" + selected.DisplayName + "</b></size>   "
                + consist.CarCount + " cars, " + consist.Engines.Count + " engines");
            builder.AddLabel("<color=" + DimColor + ">Numbered from the front of " + selected.DisplayName + ".</color>");
            builder.Spacer(6f);

            // The scroll view stretches to fill the space between header and footer.
            builder.VScrollView(delegate (UIPanelBuilder list)
            {
                list.ConfigureLayout(spacing: 14f);
                foreach (EngineInfo engine in consist.Engines)
                {
                    AddEngineRow(list, engine, engine.Locomotive == selected, plan);
                }
            });

            builder.Spacer(6f);
            AddMessages(builder, plan);

            builder.ButtonStrip(delegate (UIPanelBuilder strip)
            {
                strip.AddButton("Link helpers", () => LinkPressed(consist)).Disable(!plan.IsValid);
                strip.AddButton("Unlink all", () => UnlinkPressed(consist)).Disable(!hasLinks);
                strip.AddButton("Reset", () =>
                {
                    ResetDraft();
                    SetNeedsRebuild();
                });
            });
        }

        // Keeps the player's unsaved choices while the window rebuilds for
        // unrelated reasons (MU flags, air), and reloads them from the
        // consist's links (or defaults) when the cars or links change.
        private static void SyncDraft(ConsistInfo consist)
        {
            string key = consist.Selected.id + "|" + string.Join(",", consist.Engines.Select(e =>
                e.Locomotive.id + "/" + e.LinkedLeadId + "/" + (int)e.LinkedRole));
            if (key == _draftKey) return;
            _draftKey = key;
            _draft = LinkPlanner.ExistingRoles(consist) ?? LinkPlanner.DefaultRoles(consist);
        }

        // One engine: position on the left, name and details in the middle,
        // role picker on the right.
        private static void AddEngineRow(UIPanelBuilder list, EngineInfo engine, bool isSelected, LinkPlan plan)
        {
            list.HStack(delegate (UIPanelBuilder row)
            {
                row.ConfigureLayout(spacing: 12f, childAlignment: TextAnchor.MiddleLeft);

                row.AddLabel("<size=130%><b>#" + engine.Position + "</b></size>").Width(56f);

                row.VStack(delegate (UIPanelBuilder text)
                {
                    text.ConfigureLayout(spacing: 2f);
                    string name = "<size=115%><b>" + engine.Name + "</b></size>";
                    if (isSelected) name += "   <color=" + SelectedColor + ">Selected</color>";
                    if (plan.Controller == engine.Locomotive) name += "   <color=" + MuColor + ">In control</color>";
                    text.AddLabel(name);

                    string facing = isSelected ? "" : (engine.FacesSameWayAsSelected ? "  ·  Same way" : "  ·  Reversed");
                    text.AddLabel("<color=" + DimColor + ">" + engine.Model + "  ·  " + (engine.IsSteam ? "Steam" : "Diesel") + facing + "</color>");
                    text.AddLabel(StateLine(engine));
                }).FlexibleWidth();

                HelperRole role = _draft.TryGetValue(engine.Locomotive.id, out HelperRole r) ? r : HelperRole.None;
                string id = engine.Locomotive.id;
                row.AddDropdown(RoleNames, (int)role, delegate (int index)
                {
                    _draft[id] = (HelperRole)index;
                    SetNeedsRebuild();
                }).Width(120f);
            });
        }

        private static string StateLine(EngineInfo engine)
        {
            string state;
            if (engine.LinkedLeadId != null)
            {
                string leadName = TrainController.Shared.TryGetCarForId(engine.LinkedLeadId, out Car lead) ? lead.DisplayName : "?";
                state = "<color=" + MuColor + ">Linked to " + leadName + "</color>";
            }
            else if (engine.IsMu) state = "<color=" + MuColor + ">MU</color>";
            else if (engine.IsCutOut) state = "<color=" + DimColor + ">Cut out</color>";
            else state = "<color=" + DimColor + ">Cut in</color>";

            if (!engine.AirConnectedToSelected)
            {
                state += "  <color=" + WarnColor + ">· Air not connected to the selected engine</color>";
            }
            return state;
        }

        private static void AddMessages(UIPanelBuilder builder, LinkPlan plan)
        {
            foreach (string error in plan.Errors) builder.AddLabel("<color=" + ErrorColor + ">" + error + "</color>");
            foreach (string warning in plan.Warnings) builder.AddLabel("<color=" + WarnColor + ">" + warning + "</color>");
            foreach (string note in plan.Notes) builder.AddLabel("<color=" + DimColor + ">" + note + "</color>");
        }

        // Re-reads and re-plans at press time rather than trusting the plan
        // the buttons were built with: the consist may have changed since.
        private static void LinkPressed(ConsistInfo shown)
        {
            ConsistInfo consist = ConsistReader.Read(shown.Selected);
            LinkPlan plan = LinkPlanner.Plan(consist, _draft);
            if (!plan.IsValid)
            {
                Toast.Present("Helper Manager: " + (plan.Errors.FirstOrDefault() ?? "can't link."));
                SetNeedsRebuild();
                return;
            }
            HelperLinks.Apply(plan);
            HelperLinks.Rescan(allowRepair: false);
            ResetDraft();
            SetNeedsRebuild();

            int helpers = plan.Linked.Values.Count(r => r != HelperRole.Lead);
            Toast.Present(helpers + " helper" + (helpers == 1 ? "" : "s") + " linked to " + plan.Controller.DisplayName + ".");
        }

        private static void UnlinkPressed(ConsistInfo shown)
        {
            ConsistInfo consist = ConsistReader.Read(shown.Selected);
            int count = 0;
            foreach (EngineInfo engine in consist.Engines)
            {
                if (engine.LinkedLeadId == null) continue;
                HelperLinks.Unlink(engine.Locomotive);
                count++;
            }
            HelperLinks.Rescan(allowRepair: false);
            ResetDraft();
            SetNeedsRebuild();
            Toast.Present(count + " engine" + (count == 1 ? "" : "s") + " unlinked.");
        }
    }
}
