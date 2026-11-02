using System.Collections.Generic;
using System.Linq;
using Model;

namespace HelperManager
{
    // What pressing Link would do, plus anything that blocks it or that the
    // player should know about.
    internal sealed class LinkPlan
    {
        public BaseLocomotive Controller;
        public Dictionary<BaseLocomotive, HelperRole> Linked = new Dictionary<BaseLocomotive, HelperRole>();
        public List<BaseLocomotive> Unlink = new List<BaseLocomotive>();
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<string> Notes = new List<string>();

        public bool IsValid => Errors.Count == 0 && Controller != null;
    }

    internal static class LinkPlanner
    {
        // Roles for a consist with no links yet: the selected engine's group
        // leads, the farthest group behind it pushes, everything else is mid.
        public static Dictionary<string, HelperRole> DefaultRoles(ConsistInfo consist)
        {
            var roles = new Dictionary<string, HelperRole>();
            EngineInfo selected = consist.Engines.FirstOrDefault(e => e.Locomotive == consist.Selected);
            if (selected == null) return roles;

            int leadGroup = selected.Group;
            int rearGroup = consist.Groups.Count - 1;
            foreach (EngineInfo engine in consist.Engines)
            {
                HelperRole role;
                if (engine.Group == leadGroup) role = HelperRole.Lead;
                else if (engine.Group == rearGroup && rearGroup > leadGroup) role = HelperRole.Rear;
                else role = HelperRole.Mid;
                roles[engine.Locomotive.id] = role;
            }
            return roles;
        }

        // Roles matching the links already on this consist, or null if it has
        // none. When the selected engine belongs to a linked group, that
        // group's lead is shown as Lead.
        public static Dictionary<string, HelperRole> ExistingRoles(ConsistInfo consist)
        {
            var idsInConsist = new HashSet<string>(consist.Engines.Select(e => e.Locomotive.id));
            List<EngineInfo> linked = consist.Engines
                .Where(e => e.LinkedLeadId != null && idsInConsist.Contains(e.LinkedLeadId))
                .ToList();
            if (linked.Count == 0) return null;

            string selectedId = consist.Selected.id;
            string leadId = linked.Any(e => e.LinkedLeadId == selectedId)
                ? selectedId
                : linked.FirstOrDefault(e => e.Locomotive.id == selectedId)?.LinkedLeadId ?? linked[0].LinkedLeadId;

            var roles = new Dictionary<string, HelperRole>();
            foreach (EngineInfo engine in consist.Engines) roles[engine.Locomotive.id] = HelperRole.None;
            foreach (EngineInfo engine in linked) roles[engine.Locomotive.id] = engine.LinkedRole;
            roles[leadId] = HelperRole.Lead;
            return roles;
        }

        public static LinkPlan Plan(ConsistInfo consist, IReadOnlyDictionary<string, HelperRole> roles)
        {
            var plan = new LinkPlan();
            HelperRole RoleOf(EngineInfo e) => roles.TryGetValue(e.Locomotive.id, out HelperRole r) ? r : HelperRole.None;

            List<EngineInfo> leads = consist.Engines.Where(e => RoleOf(e) == HelperRole.Lead).ToList();
            List<EngineInfo> helpers = consist.Engines.Where(e => RoleOf(e) == HelperRole.Mid || RoleOf(e) == HelperRole.Rear).ToList();

            if (leads.Count == 0) plan.Errors.Add("Choose a Lead engine.");
            if (helpers.Count == 0) plan.Errors.Add("Choose at least one Mid or Rear helper.");
            if (leads.Select(e => e.Group).Distinct().Count() > 1)
            {
                plan.Errors.Add("Lead engines must be coupled directly together, with only engines or tenders between them.");
            }
            if (leads.Count == 0) return plan;

            // The controller is the lead engine not in MU state, as vanilla
            // would have it. Ties go to the selected engine, then the front.
            List<EngineInfo> inControl = leads.Where(e => !e.IsMu && !e.IsCutOut).ToList();
            List<EngineInfo> pool = inControl.Count > 0 ? inControl : leads;
            EngineInfo controller = pool.FirstOrDefault(e => e.Locomotive == consist.Selected) ?? pool[0];
            plan.Controller = controller.Locomotive;
            if (controller.IsMu || controller.IsCutOut)
            {
                plan.Notes.Add(controller.Name + " will be cut in and take control.");
            }

            foreach (EngineInfo engine in leads)
            {
                if (engine != controller) plan.Linked[engine.Locomotive] = HelperRole.Lead;
            }
            foreach (EngineInfo engine in helpers)
            {
                plan.Linked[engine.Locomotive] = RoleOf(engine);
            }
            foreach (EngineInfo engine in consist.Engines)
            {
                if (RoleOf(engine) == HelperRole.None && engine.LinkedLeadId != null) plan.Unlink.Add(engine.Locomotive);
            }

            // Brakes: helpers are cut out, so the lead's automatic brake has to
            // reach them through the brake pipe.
            var airFromController = new HashSet<Car>(controller.Locomotive.EnumerateAirOpen());
            foreach (EngineInfo engine in helpers)
            {
                if (!airFromController.Contains(engine.Locomotive))
                {
                    plan.Warnings.Add(engine.Name + ": air isn't connected to the lead, so train brakes won't reach it.");
                }
            }

            // Positions only produce warnings; Mid and Rear behave the same.
            EngineGroup leadGroup = consist.Groups[controller.Group];
            bool leadAtFront = leadGroup.First == 1;
            bool leadAtBack = leadGroup.Last == consist.CarCount;
            if (!leadAtFront && !leadAtBack)
            {
                plan.Warnings.Add("The lead isn't at either end of the train.");
            }
            int rearGroup = leadAtFront || !leadAtBack ? consist.Groups.Count - 1 : 0;
            if (rearGroup != controller.Group)
            {
                foreach (EngineInfo engine in helpers)
                {
                    if (RoleOf(engine) == HelperRole.Rear && engine.Group != rearGroup)
                    {
                        plan.Warnings.Add(engine.Name + " is marked Rear but isn't in the last group of engines.");
                    }
                }
            }
            return plan;
        }
    }
}
