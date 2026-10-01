using BOCCHI.Common.Config.Renderers;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config.Fields;

public sealed class BossModPresetOptionsAttribute()
    : UIFieldAttribute(typeof(BossModPresetOptionsRenderer));

public sealed class DisabledCriticalEncounterIdsAttribute()
    : UIFieldAttribute(typeof(DisabledCriticalEncounterIdsRenderer));

public sealed class DisabledFateIdsAttribute()
    : UIFieldAttribute(typeof(DisabledFateIdsRenderer));

public sealed class FarmSpotListAttribute()
    : UIFieldAttribute(typeof(FarmSpotListRenderer));

public sealed class LogsViewerAttribute()
    : UIFieldAttribute(typeof(LogsViewerRenderer));

public sealed class MobMultiSelectAttribute()
    : UIFieldAttribute(typeof(MobMultiSelectRenderer));

public sealed class MountSelectAttribute()
    : UIFieldAttribute(typeof(MountSelectRenderer));

public sealed class Mp3SoundSelectAttribute()
    : UIFieldAttribute(typeof(Mp3SoundSelectRenderer));

public sealed class PluginDependencyStatusAttribute()
    : UIFieldAttribute(typeof(PluginDependencyStatusRenderer));

public sealed class TriageRaiseJobAttribute()
    : UIFieldAttribute(typeof(TriageRaiseJobRenderer));

public sealed class WrathOccultOptionBlacklistAttribute()
    : UIFieldAttribute(typeof(WrathOccultOptionBlacklistRenderer));
