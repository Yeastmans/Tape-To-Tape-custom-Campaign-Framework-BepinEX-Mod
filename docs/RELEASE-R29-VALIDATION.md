# Revision 29 description validation

Custom relic, regular talent, Chaos talent and goalie talent descriptions follow the concise style of the native reward catalog. Values, recipients, conditions, duration, stacking and activation limits remain explicit where they affect the rule. Authored descriptions, gameplay mechanics, item IDs and serialization are unchanged. Creator help retains the detailed explanations.

The current task passed 1,135 focused CustomRewardCreatorChecks, including new checks that first failed against the longer descriptions. The three changed production source files match the tested files exactly; all other tracked source and asset inputs match revision 28. These results are reused for the release without rerunning unrelated tests.

A sweep of 6,433 valid generated examples reduced average description length from 205.8 to 90.4 characters (56.1%). This is text inspection, not gameplay observation. The release builds a fresh mod and self-contained Windows Studio, bundles their matching runtime, and verifies installer contents and public asset hashes before completion. Receipts are kept under artifacts/release-r29-reward-descriptions-20261026 in the canonical workspace.

To check the update, save edits and close Studio and Tape to Tape, then run revision 29 Setup. Open existing custom relics and talents in Studio and check that the effect, chance, recipient and timing are readable and that any custom written description remains. In the game, inspect their reward cards or tooltips. Confirm examples with temporary bonuses, stacking, opposing recipients and permanent gains retain their meaning. Live installation and gameplay are not claimed as observed by the automated checks.
