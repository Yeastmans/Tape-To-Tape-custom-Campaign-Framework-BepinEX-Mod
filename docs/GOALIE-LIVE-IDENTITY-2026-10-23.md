# Live goalie identity investigation

The reviewed 26150666 runtime reached live play with the Gold OneMatch goalie,
but rejected its live binding before actual match completion. The observed
0-0 screen was the transition into overtime, not a completed match. No contract
debit or expiry acceptance is established by that session.

The old message combined several distinct failures: manager, controller campaign,
game type, selected team, goalie receiver, two live data pointers and stable
goalie ID. It did not prove that the goalie ID itself changed. Display names are
not part of these predicates. Native OnFaceoff and OnPuckDropLaunch both set
isGamePlaying before returning, so ignoring non-playing callbacks is not a
supported remedy. A read-only native probe after restart found no live match
manager and cannot reconstruct the earlier binding.

This diagnostic change retains every existing fail-closed predicate and adds one
bounded warning before invalidation. `[GoalieIdentity]` records the first failed
predicate, expected and current native pointers, goalie IDs, run and attempt
token. Diagnostic failure cannot prevent invalidation. No hook, profile, save
format or contract-accounting behavior changes.

Two regression assertions reproduce absent diagnostic evidence before the change:
one checks an actual stable-ID mismatch, and one checks an unproven same-ID clone.
Both cases must still block completion. The next controlled live attempt must
capture this warning before an identity repair can be justified. Do not relax
pointer equality or fabricate a completion from the persisted Armed record.

Evidence is under canonical `artifacts/goalie-live-identity-20261023`; the
controller preserves original logs/saves in
`artifacts/gameplay-validation-20261023/after-live-regulation`.
