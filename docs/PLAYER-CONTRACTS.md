# Player contract duration

In the **Player editor → Details & attributes**, turn on **Custom contract duration** and enter the number of matches the player plays before the contract ends. Save the player and campaign.

- Any skater can have a custom duration, including a starting player, a chosen bench player, a superstar or a recruited free agent. GM squad behavior is not required. Goalies can also opt in; their default duration is four played matches.
- Turning the toggle off uses normal game rules for skaters. For goalies, off means permanent until replaced.
- Only matches the player actually plays count. Matches spent on the bench or as an unused reserve goalie do not count.
- Changes apply to new runs and future recruits. Existing signed players retain their saved duration and progress, including when continuing a run after editing the campaign.
- Recruitment offers display the duration they will sign with. Turning the setting off later does not cancel an already signed contract.
- The native departure rules still apply, including effects such as Handcuffs that prevent players from leaving.
- Two-line squads retain the surviving players' positions when a contract ends; vacancies on either line can be filled through the existing placement screen.

The text fields are `Custom Contract = yes/no` and `Contract Matches = <positive whole number>`. An absent or disabled setting leaves native contracts unchanged. Invalid hand-edited durations are not treated as zero-match contracts.

## Goalie expiry and replacement

A goalie keeps the duration signed at fresh-run creation or confirmed recruitment. Swapping active and reserve moves the complete goalie record, including progress; it never starts a new contract. Continue uses that saved record. Recruitment offers and the slot confirmation show the frozen duration that will be signed.

When an unprotected active goalie's contract expires, an available, unexpired reserve becomes active. With no available replacement the expired goalie stays active, marked **Expired — awaiting replacement**. An expired reserve departs when departures are allowed. Native Handcuffs and custom effects that prevent contract departure retain the goalies without resetting their played-match progress.

Signing into Reserve while the active is expired may promote the new recruit immediately. The confirmation explains the final slots before signing: either the recruit becomes active and Reserve becomes empty, or departure protection retains the current active and the recruit stays in Reserve. If protection changes, confirm the updated result. If protection cannot be verified, no reserve signing is made. Explicitly replacing Active remains an ordinary player-chosen replacement.

The editor writes four when a new custom duration is enabled. Disabled, absent, or invalid manual durations do not create temporary goalie contracts; enabled malformed values produce a runtime warning and keep a new goalie permanent until replaced. Correct the authoring before starting a fresh run or generating a future offer.

## Release validation

The goalie policy, authoring and signing unit is implemented behind the pending runtime compatibility gate. The durable launch and actual-participation/completion units remain required before goalie match accounting is enabled. Pure policy checks, boundary doubles and wrapper builds are not native gameplay proof.

Policy and lifecycle adapters, the actual Studio controls, upgrade cleanup, portable campaign sharing, existing recruitment controls and two-line placement have focused automated checks. Native bodies and layouts were inspected on Steam roguelite build **26020247**. These checks do not replace observing gameplay on the published installer.

Human validation remains pending. On a disposable new run made with the published build:

1. Give one starter a two-match contract and another a four-match contract. Disable GM behavior. Play only the first skater for one match; confirm only that contract loses a match.
2. Put the first skater on the bench for a match. Confirm the remaining duration stays unchanged. Play them again and confirm the native departure notification and roster removal, without a departure-preventing relic.
3. Save after one played match, edit the authored duration, then Continue. Confirm the saved remaining duration is unchanged. Start another new run and confirm the edited duration applies there.
4. Recruit an enabled skater in both GM and non-GM campaigns. Confirm the offer and signed contract agree. Repeat with the toggle off and confirm ordinary game behavior. Test a selected bench player and superstar at run start.
5. On a two-line squad, expire a skater on each line, including the last custom contract. Confirm the other positions survive and a recruit can fill either vacancy. Check Continue after the departure.
6. Repeat with a native departure-preventing effect and confirm the game retains that effect. Check normal campaigns and Play Now for unintended changes.

The release process does not launch the installed game, modify real saves, or mark these observations as passed.
