using CampaignCore;
namespace HarmonyLib {
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type,string method){} }
    public class HarmonyPrefix : Attribute {}
}
namespace Rogue { public class Talent { public string id; public bool PayoffSuppressed; } }
namespace Data {
    public class TalentList : List<Rogue.Talent> { public bool HasMatch(Func<Rogue.Talent,bool> predicate) => this.Any(predicate); }
    public class SkaterData { public TalentList powerups = new(); }
}
namespace Skaters {
    public class Skater { public IntPtr Pointer=(IntPtr)1234; public Data.SkaterData _skaterData=new(); public bool ArePowerUpsEnabled()=>true; }
    public class Forward : Skater {}
}
namespace UnityEngine { public static class Time { public static float time; } }
namespace Movements.MovementStates {
    public class SlapshotState {
        public int Reads; public bool ThrowOnRead;
        public Skaters.Forward Player=new();
        public Skaters.Forward forward { get { Reads++; if(ThrowOnRead)throw new InvalidOperationException("Native forward getter touched");return Player; } }
    }
}
namespace EndlessMode {
    internal static class CustomRewardRuntime {
        internal static bool InMatch;
        internal static List<CustomRewardDefinition> Conditions=new();
        internal static IEnumerable<CustomRewardDefinition> ActiveConditions(Skaters.Skater player)=>Conditions;
    }
}
