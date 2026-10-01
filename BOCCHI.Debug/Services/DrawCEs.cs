using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.KnowledgeCrystals;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using Ocelot.Graphics;
using Ocelot.Lifecycle;
using Ocelot.Services.OverlayRenderer;
using System.Numerics;

namespace BOCCHI.Debug.Services;

public class DrawCEs(IOverlayRenderer overlay, ICriticalEncounterRepository ces, IZoneProvider zones) : IOnRender
{
    public void Render()
    {
        foreach (CriticalEncounter ce in ces.SnapshotWithoutForkedTower())
        {
            float padded = ce.Radius;
            float yellow = NavigationConstants.CriticalEncounterYellowRadius(padded);
            float red = NavigationConstants.CriticalEncounterRedRadius(padded, ce.AreaShape);
            float stand = NavigationConstants.CriticalEncounterStandRadius(red, ce.AreaShape);

            if (ce.AreaShape == ActivityAreaShape.Square)
            {
                StrokeSquare(ce.RegistrationCenter, padded, Color.Green);
                StrokeSquare(ce.RegistrationCenter, yellow, new(1f, 1f, 0f));
                StrokeSquare(ce.RegistrationCenter, red, Color.Red);
                StrokeSquare(ce.RegistrationCenter, stand, new(0f, 1f, 1f));
            }
            else
            {
                overlay.StrokeCircle(ce.RegistrationCenter, padded, Color.Green);
                overlay.StrokeCircle(ce.RegistrationCenter, yellow, new(1f, 1f, 0f));
                overlay.StrokeCircle(ce.RegistrationCenter, red, Color.Red);
                overlay.StrokeCircle(ce.RegistrationCenter, stand, new(0f, 1f, 1f));
            }
        }

        foreach (KnowledgeCrystalData crystal in zones.GetZone().GetNearbyKnowledgeCrystals())
        {
            overlay.StrokeCircle(crystal.Position, 5f, new(1f, 0f, 1f));
        }

        IZone zone = zones.GetZone();
        if (zone.IsOccultCrescentZone() && zone.IsInBasecamp())
        {
            AethernetData main = zone.GetMainAetheryte();
            overlay.StrokeCircle(main.Position, main.GetBodyRadius(), new(1f, 0f, 1f));
            overlay.StrokeCircle(main.Position, main.GetIdleOuterRadius(), new(0f, 1f, 1f));
        }

#if DEBUG
        foreach (List<Vector3> points in GraphConfig.DebugPathLines)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                overlay.StrokeLine(points[i], points[i + 1], new(1f, 0f, 0f));
            }
        }
#endif
    }

    private void StrokeSquare(Vector3 center, float halfExtent, Color color)
    {
        float y = center.Y;
        Vector3 nw = new(center.X - halfExtent, y, center.Z - halfExtent);
        Vector3 ne = new(center.X + halfExtent, y, center.Z - halfExtent);
        Vector3 se = new(center.X + halfExtent, y, center.Z + halfExtent);
        Vector3 sw = new(center.X - halfExtent, y, center.Z + halfExtent);
        overlay.StrokeLine(nw, ne, color);
        overlay.StrokeLine(ne, se, color);
        overlay.StrokeLine(se, sw, color);
        overlay.StrokeLine(sw, nw, color);
    }
}
