# Revision 34 validation

Adds **Base Game Pools** on Studio Home, with three sections: bench/draft candidates, free agents, and superstars. Each starts with the base-game selection. Add, replace, edit, or deselect players, then save the pools for **None (base game)**. You can add Charlotte while keeping Boring Dude. Teams share the edited bench candidates and keep their normal bench size, roster checks, and no-bench rules. Each saved pool requires at least one candidate.

When no selected superstar is eligible, the campaign continues without one and keeps its other starting rewards. Damaged pool settings can be recovered through a defaults draft that you review and save.

Updates compatibility for **Tape to Tape Steam build 26372689**, retaining **1,507 protected native methods**. Revision 33 unlimited campaign upload support is included.

Fixes the charged-release hook responsible for the reproduced Hockey FC campaign crash. The repaired hook completed a full two-period FC match and the normal post-match transition, with no access violation recorded. The release validation document distinguishes that unchanged-component test from the final pool tests.

Component evidence may come from an earlier candidate when source-delta review establishes that component is unchanged. The final zero-eligible Old School continuation requires a new run on the final runtime; the release validation records each observation's actual runtime.

Final Base Game pool smoke: Charlotte and Boring Dude together; shared All Angus candidates and native roster validation; one/two-star offer selection; Defense zero-star startup; Old School zero-star startup retaining Dash, relic and three native starters with no bench; two independent custom free-agent offers with the chosen recruit saved. The final F884 run verifies cache recovery and recruitment; earlier unchanged paths are attributed separately below.

Validation: 76 editor/profile checks, 24 player-editor checks, 137 source-linked pool runtime checks, 149 compatibility checks, 26 charged-release checks, independent verification of all 1,507 native contracts, and independent compiled/source reviews passed. Native tests were muted and captured with Steam F12. Package and public-download verification accompany publication; no elevated installer execution is claimed.

Install **T2T_Custom_Campaign_Framework_Setup.exe**, or use Studio's update check. Installer extraction verifies packaged files and both runtime destinations; it does not claim an elevated installer run.


## Native evidence attribution

| Case | Executed runtime SHA-256 prefix | Evidence |
| --- | --- | --- |
| bench10CharlotteAndBoring | `EDBA5F282A12` | earlier candidate; unchanged component reviewed |
| defenseZeroEligible | `EDBA5F282A12` | earlier candidate; unchanged component reviewed |
| allAngusSharedBench | `83A0615F9B2B` | earlier candidate; unchanged component reviewed |
| oneStarOffer | `83A0615F9B2B` | earlier candidate; unchanged component reviewed |
| twoStarOffers | `83A0615F9B2B` | earlier candidate; unchanged component reviewed |
| oldSchoolZeroEligibleRewards | `83A0615F9B2B` | earlier candidate; unchanged component reviewed |
| nativeNoBench | `83A0615F9B2B` | earlier candidate; unchanged component reviewed |
| gmTwoIndependentOffers | `F88446C6B028` | final candidate |
| fcRepairedFullMatch | `ABE80BCCAEAB` | earlier candidate; unchanged component reviewed |

The elevated installer itself was not executed during extraction checks. Native runtime evidence and package verification are separate.
