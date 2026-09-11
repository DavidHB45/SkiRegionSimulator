namespace AlpineSim.Core.Save
{
    /// <summary>
    /// Save-file schema version. Bump when WorldState changes shape in a way a migration must handle.
    /// v1: initial (rng stored as "RngState": [s0, s1]; no Log).
    /// v2: rng stored as object {S0, S1}; Log list; ScenarioId required.
    /// v3: LiftState.PlayerClosed, EconomyState.LastCloseTick.
    /// v4: VehicleAiState.LanesSkipped, RefuseTimer, StuckCount, FuelDeniedTimer, JobMode, LaneAbandoned, ParkedStuckTick, TopDown, ServiceCall; WorkTask.BlockedTick; SurfaceZone.SnowScore; FuelDepotState.AutoOrder.
    /// v5: WorkTask.BlockedByVehicleId and BlockedByTick (the machine that handed a job back and when, so the foreman does not offer it straight back).
    /// </summary>
    public static class SaveSchema
    {
        public const int CurrentVersion = 5;
    }
}
