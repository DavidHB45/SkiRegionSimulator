namespace AlpineSim.Core.Snow
{
    /// <summary>What a snow cell sits on. Stored per cell; drives which mutators and scores apply.</summary>
    public enum SurfaceType : byte
    {
        OffPiste = 0,
        Piste = 1,
        Road = 2,
        Lot = 3,
        LiftRamp = 4,
        NordicTrail = 5,
        Park = 6,
        BaseArea = 7,
    }
}
