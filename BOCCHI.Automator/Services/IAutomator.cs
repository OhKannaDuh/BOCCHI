using BOCCHI.Common.Services;
﻿using BOCCHI.Automator.Data;

namespace BOCCHI.Automator.Services;

public interface IAutomator : IPathMapRebuilder
{
    bool Enabled { get; }

    bool IsActive { get; }

    bool IsPotsAndTreasure { get; }

    bool IsCompletionist { get; }

    bool SuspendedForTreasure { get; }

    bool SuspendedForShopping { get; }

    bool IsIllegalMode { get; }

    void SetSuspendedForTreasure(bool suspended);

    void SetSuspendedForShopping(bool suspended);

    void SoftStopPathfinding();

    AutomatorState? CurrentState { get; }

    void Toggle();

    void TogglePotsAndTreasure();

    void ToggleCompletionist();


    void Render();
}
