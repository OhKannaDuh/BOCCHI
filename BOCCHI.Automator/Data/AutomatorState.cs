namespace BOCCHI.Automator.Data;

public enum AutomatorState
{
    // A no op state that the state machine starts in.
    Entry,
    // We currently have nothing to do. This starts a timer so we can track idle time
    Idle,
    // Our character has died, determine whether to release or wait for a raise
    Dead,
    // In combat but not in a fate, CE or mob farm. We should finish that combat before moving on to prevent issues.
    InCombat,

    // Cast the treasure sight skill to get a coffer count
    CastingTreasureSight,
    // Apply buffs at a knowledge crystal
    ApplyingBuffs,

    // Repair our equipped gear
    Repairing,
    // Extract materia from our equipped gear
    ExtractingMateria,

    // Deciding if we should do fates/CEs/Mob farming/Tresure hunting etc.
    ChoosingActivity,

    // Return to basecamp
    Returning,
    // Moving from A to B in a way that makes sense
    Pathfinding,

    // We are currently participating in a fate
    InFate,
    // We are in the CE zone and waiting for it to start
    // WaitingForCriticalEncounter,
    // We are currently participating in a CE
    // InCriticalEncounter,

    // Returning to the job we originally were based on a memory
    ReturningToJob,

    // Casting raise on dead players around us
    // RaisingTheDead,

    // Change OC instance
    // Reinstancing,
    // Searching the OC for treasure chests
    // TreasureHunting,
    // Searching the OC for Carrots
    // CarrotHunting,
    // Farming mobs in the OC
    // MobFarming
    // Searching the OC for Pot chests
    // PotChestHunting,

    // We are the only one at a fate and it has 20 million hp, let's reset to change the scaling
    // ResettingFateHp
}
