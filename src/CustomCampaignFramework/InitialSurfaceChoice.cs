namespace EndlessMode;

internal static class InitialSurfaceChoice
{
    // IceSetup.IceType: None=0, Standard=1, Golf=2, Soccer=3, Tycoons=4.
    // Change only the first load. Native period refreshes and relic-triggered
    // switches retain their own lifecycle after initialization.
    internal static int Resolve(int nativeType, bool defaultMode, bool liveRink,
        bool initialized, bool switching, int currentType, string surface, bool soccerNode)
    {
        if (defaultMode || !liveRink || initialized || switching || currentType != 0)
            return nativeType;
        return surface switch
        {
            "Standard ice" => 1,
            "Golf ice" => 2,
            "Soccer field" => 3,
            "Tycoons ice" => 4,
            "" or null when soccerNode => 3,
            _ => nativeType
        };
    }
}
