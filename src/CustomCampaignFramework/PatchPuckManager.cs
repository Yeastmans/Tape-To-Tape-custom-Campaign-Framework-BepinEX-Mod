using System;
using Tape2Tape;

namespace EndlessMode;

// OpponentMatch resolves the editor's Match Puck independently of Match Surface.
// An empty selection means an unconfigured native match; leave its prefabs alone.
public static class PatchPuckManager
{
    public sealed class OriginalPucks
    {
        public PuckScriptableObject Regular, Soccer, Golf;
    }

    public static bool Prefix(PuckManager __instance, out OriginalPucks __state)
    {
        __state = null;
        if (Plugin.IsDefaultMode || __instance == null) return true;
        var selected = OpponentMatch.Puck == "Soccer ball" ? __instance.soccerBall :
            OpponentMatch.Puck == "Golf ball" ? __instance.golfBall :
            OpponentMatch.Puck == "Hockey puck" ? __instance.regularPuck : null;
        if (selected == null) return true;

        // The native regular-team branch paints the team's puck texture after
        // spawning the mesh. A redirected golf prefab therefore still looks
        // like a black puck. Balls need their own prefab materials untouched.
        if (OpponentMatch.Puck != "Hockey puck")
        {
            if (Puck.Instance == null) return true;
            Puck.Instance.Initialize(selected);
            return false;
        }

        __state = new OriginalPucks
        {
            Regular = __instance.regularPuck, Soccer = __instance.soccerBall, Golf = __instance.golfBall
        };
        // Native initialization chooses by team identity. Route every choice to
        // the selected prefab for this call, then restore even if it throws.
        __instance.regularPuck = __instance.soccerBall = __instance.golfBall = selected;
        return true;
    }

    public static Exception Finalizer(PuckManager __instance, OriginalPucks __state, Exception __exception)
    {
        if (__state != null && __instance != null)
        {
            __instance.regularPuck = __state.Regular;
            __instance.soccerBall = __state.Soccer;
            __instance.golfBall = __state.Golf;
        }
        return __exception;
    }
}
