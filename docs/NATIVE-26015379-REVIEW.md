# Native review for Steam build 26015379

GameAssembly: `2A6EB5B74057D101BD17D2E19F8A924E62FAEE1C02F17FDF975A53C744A6932E`.
Metadata: `353CA20EE233F38EF35CCBA4041E2EA99B8C115A88D75ECFC1C7A699A5BD6A18`.

The previous baseline rejected this update. The replacement retains all 266
protected methods, 142 comparisons and 110 squad-query callers. Signatures match;
45 normalized body hashes differ. Retained canonical records from 26001062 verify
the exact prior bodies for 42 of these entries. Most differences are relocated
runtime thunks or changed aliases of shared constructors/disposal methods.
Additional reviewed current disassembly covers recruit stat scaling, opponent
substitution and knockout behavior. The production verifier remains unchanged.

Four comparison indices move: FaceoffWithReferee.OnEndOfReplay 25 -> 26,
GoalReplay.Awake 71 -> 83, GameplaySettingsUI.InitializeCameraModeOptions 97 -> 99,
and BlademasterMapNode.CreateNodeReward 107 -> 89. Fresh discovery preserves exact
owner/query/kind occurrence coverage and the five-byte native call ABI. Replay
initialization adds native instance registration. Camera selection inlines its
getter. Blademaster construction and upgrade use native helpers instead of the
prior inline code. The routed predicates still govern the same feature branches.

The starting hook remains `410FB6C60A4758`, now at `0x3B90962`. R14B, the RDI
state-machine flags at +0x58/+0x59, RSI title instance and the following branch
retain their roles. The surface prologue remains unchanged at `0x39A8090`.
MapObject destruction adds owner-specific native singleton cleanup before its
existing cleanup. Current-node assignment, intro ownership and custom cleanup
hooks retain their order and scope. Rebuilt field accessors use current interop.

The five custom-reward hook groups have separate exact game pins. They were
reviewed independently: all eleven native entries retain their signatures,
original prologues and behavior. The ten generic talent/relic/controller queries
retain their RVAs, MethodInfo arguments, returns, cache/list handling and native
fallback. GoalScoredArg construction moves from `0x3605D90` to `0x36008C0`; its
argument order and field writes remain unchanged. The corresponding hash pins
and goal-constructor RVA are updated; MethodInfo pointer checks and prologue
checks remain enforced. No global compatibility bypass was added.

Evidence is under `artifacts/game-update-26015379-20261017/`: original rejection,
full method/site audit, exact canonical comparison, annotated native entries,
current dump, source checks and native test records. Binary review is distinct
from runtime installation and behavioral checks recorded there.

Final candidate runtime SHA-256:
`DFD176CEFC8719638B5A30C064AE0800B081F0FAE6D4B83A3ADAEC8802426A12`.
The actual game installed all 143 routed branches and all eleven reward detours.
Eight off/on callbacks cover the four moved comparisons. Thirty-two native GM
offers cover both recruit sources, both stat settings, both extra-talent settings,
fresh/cached offers and selected/native pools. Fresh selected pools remain
allow-lists and can yield no eligible extra; cached extras reappear when enabled.
The live native talent/relic/controller fallbacks and goal-constructor argument
layout passed. The constructor check did not dispatch a goal event.

Two actual QA match starts passed, with player line switching and active stamina.
A temporary opponent fixture spawned ten skaters, retained eleven distinct menu
cards including the goalie, and switched line 1 -> 2 -> 1. Ten contract badges
were present in each tested player roster view. These are API-driven native
integration checks, not a manual playthrough or validation of every custom reward.
No production Error/Fatal entries or unavailable hook groups appeared. Existing
Harmony reflection warnings remain. A temporary probe command issued before map
loading completed was rejected; it passed after the map finished loading.
All 1,705 freshly backed-up files were restored and hash-verified after testing.
