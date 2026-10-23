// Only the data needed to exercise the production scoring transaction. These
// do not simulate native scoring, goal-denial UI or Harmony installation.
namespace NHL94 { public enum GoalType { GOOD, BAD } }
namespace Skaters { public class Skater { public Tape2Tape.Team Team; } }
namespace UnityEngine { public static class Time { public static int frameCount; } }
namespace Events
{
    public sealed class GoalScoredArg
    {
        private static int sequence;
        public IntPtr Pointer = (IntPtr)(++sequence);
        public NHL94.GoalType goalType;
        public Skaters.Skater scorer;
    }
}
namespace Rogue.Relics.Components
{
    public class GoalDenierComponent
    {
        public IntPtr Pointer;
        public Tape2Tape.Team _team;
        public object _disableActionCoroutine;
        private void OnGoalScored(Events.GoalScoredArg _) { }
        private void DisableInput() { }
    }
}
public class GameStats { private void OnDeniedGoal() { } }

namespace Data { public class TeamData { public string id = Guid.NewGuid().ToString(); } }
