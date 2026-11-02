using System.Collections.Generic;
using System.Linq;
using System.Text;
using Model;

namespace HelperManager
{
    // One engine in a consist, as shown in the window.
    internal sealed class EngineInfo
    {
        public BaseLocomotive Locomotive;
        public int Position;          // 1-based, counted from the front of the selected engine
        public int Group;             // index into ConsistInfo.Groups
        public string Name;
        public string Model;
        public bool IsSteam;
        public bool FacesSameWayAsSelected;
        public bool IsMu;
        public bool IsCutOut;
        public bool AirConnectedToSelected;
        public string LinkedLeadId;   // helperMgr.lead, or null
        public HelperRole LinkedRole;
    }

    // A run of engines and tenders coupled directly together, with no other
    // cars between them. Positions are 1-based and inclusive.
    internal sealed class EngineGroup
    {
        public int First;
        public int Last;
    }

    internal sealed class ConsistInfo
    {
        public Car Selected;
        public List<Car> Cars = new List<Car>();
        public List<EngineInfo> Engines = new List<EngineInfo>();
        public List<EngineGroup> Groups = new List<EngineGroup>();
        public int CarCount => Cars.Count;
    }

    internal static class ConsistReader
    {
        // Reads the cut of cars coupled to `selected`. Ordered starting from
        // the end the selected car's front points toward, so for a lead
        // engine the list reads front to back.
        public static ConsistInfo Read(Car selected)
        {
            var info = new ConsistInfo { Selected = selected };
            if (selected == null || selected.set == null) return info;

            info.Cars = selected.EnumerateCoupled(Car.End.F).ToList();
            var airOpen = new HashSet<Car>(selected.EnumerateAirOpen());

            EngineGroup group = null;
            for (int i = 0; i < info.Cars.Count; i++)
            {
                Car car = info.Cars[i];
                bool isEngineCar = car is BaseLocomotive || car.CarType.IsTender;
                if (!isEngineCar)
                {
                    group = null;
                    continue;
                }
                if (group == null)
                {
                    group = new EngineGroup { First = i + 1 };
                    info.Groups.Add(group);
                }
                group.Last = i + 1;

                if (!(car is BaseLocomotive loco)) continue;
                info.Engines.Add(new EngineInfo
                {
                    Locomotive = loco,
                    Position = i + 1,
                    Group = info.Groups.Count - 1,
                    Name = loco.DisplayName,
                    Model = ModelName(loco),
                    IsSteam = loco.CarType.IsLocomotiveSteam,
                    // A car's logical A end faces the same way for every car
                    // in an integration set, so comparing FrontIsA gives
                    // relative facing however far apart the two cars are.
                    FacesSameWayAsSelected = loco.FrontIsA == selected.FrontIsA,
                    IsMu = HelperLinks.IsMu(loco),
                    IsCutOut = HelperLinks.IsCutOut(loco),
                    AirConnectedToSelected = airOpen.Contains(loco),
                    LinkedLeadId = HelperLinks.LinkedLeadId(loco),
                    LinkedRole = HelperLinks.LinkedRole(loco),
                });
            }
            return info;
        }

        // Cheap fingerprint of everything the window displays, so it only
        // rebuilds when something actually changed.
        public static string Signature(Car selected)
        {
            if (selected == null || selected.set == null) return "";
            return Signature(Read(selected));
        }

        public static string Signature(ConsistInfo info)
        {
            var sb = new StringBuilder();
            sb.Append(info.Selected?.id).Append('|').Append(info.CarCount);
            foreach (EngineInfo e in info.Engines)
            {
                sb.Append('|').Append(e.Locomotive.id).Append(':').Append(e.Position)
                    .Append(e.IsMu ? 'M' : '-').Append(e.IsCutOut ? 'C' : '-')
                    .Append(e.AirConnectedToSelected ? 'A' : '-')
                    .Append(e.FacesSameWayAsSelected ? 'F' : 'R')
                    .Append(e.LinkedLeadId).Append('/').Append((int)e.LinkedRole);
            }
            return sb.ToString();
        }

        // The game's own display name for the model (e.g. "P-48 Pacific").
        // CarType is only a category code like "LS"/"LD".
        private static string ModelName(Car car)
        {
            string name = car.DefinitionInfo?.Metadata?.Name;
            return string.IsNullOrEmpty(name) ? car.CarType.ToString() : name;
        }
    }
}
