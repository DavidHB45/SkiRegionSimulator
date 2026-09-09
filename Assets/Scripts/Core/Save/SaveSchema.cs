namespace AlpineSim.Core.Save
{
    /// <summary>
    /// Save-file schema version. Bump when WorldState changes shape in a way a migration must handle.
    /// v1: initial (rng stored as "RngState": [s0, s1]; no Log).
    /// v2: rng stored as object {S0, S1}; Log list; ScenarioId required.
    /// </summary>
    public static class SaveSchema
    {
        public const int CurrentVersion = 2;
    }
}
