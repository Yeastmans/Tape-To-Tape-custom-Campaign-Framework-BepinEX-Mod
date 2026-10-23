namespace State.GameModeControllers {
    public class GameControllerBase : Il2CppSystem.Object { public State.CampaignState campaignState; public State.TeamSelection teamSelectionState; }
}
namespace State.GameModeControllers.NormalMatch { public class NormalMatchGameModeController : GameControllerBase {} }
namespace State.GameModeControllers.Shootout {
    public class ShootoutGameModeController : State.GameModeControllers.GameControllerBase {}
    public class InRoundState : Il2CppSystem.Object { public ShootoutGameModeController _gameModeController; }
}
namespace State { public class TeamSelection : Il2CppSystem.Object { public Data.TeamData homeTeam; public ScriptableStates.GameType gameType; } }
namespace Tape2Tape { public class Team : Il2CppSystem.Object { public Goaltender.Goaltender goaltender; public Data.TeamData _teamData; public bool isHomeTeam, isNorth; public State.TeamSelection teamSelection; } }
namespace Goaltender { public class Goaltender : Il2CppSystem.Object { public Data.GoaltenderData goaltenderData; public Data.SkaterData _skaterData; public Tape2Tape.Team Team; } }
public partial class GameManager { public static GameManager Instance; public Tape2Tape.Team _homeTeam; public bool isGamePlaying; }
public class GameLoader : Il2CppSystem.Object {
    public State.CampaignState campaignState; public State.TeamSelection teamSelectionState;
    public State.GameModeControllers.GameControllerBase _gameModeController;
}
namespace State.EndOfGame {
    public class EndOfGameFlowController : Il2CppSystem.Object {
        public EndOfGameContext _context; public State.CampaignState _campaignState; public State.TeamSelection _teamSelection;
        public State.GameModeControllers.GameControllerBase _gameModeController;
    }
    public class EndOfGameContext : Il2CppSystem.Object {
        public State.CampaignState CampaignState; public State.GameModeControllers.GameControllerBase GameModeController;
        public ScriptableStates.GameType GameType; public Il2CppSystem.Object MatchNode;
    }
}
namespace Cysharp.Threading.Tasks {
    public enum UniTaskStatus { Pending, Succeeded, Faulted, Canceled }
    public struct UniTask { public UniTaskStatus Status; }
}
