namespace LAS
{
    public enum ApproachRange
    {
        Default,   // uses ctx.ArrivalDistance
        Pickup,    // uses ctx.PickupRange
        HandOver   // uses ctx.HandOverRange
    }
}
