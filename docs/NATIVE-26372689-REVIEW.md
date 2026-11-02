# Native compatibility review: Steam build 26372689

Reviewed the installed Windows public build after Steam updated it during local testing on 2026-11-01. All review tools used immutable copies and isolated build outputs.

- GameAssembly SHA-256: `F756BCB8564C2D95C60A121C7A5EBB21DC1B0418421FAF03F4925139944E6142`.
- Metadata SHA-256: `1AC5E68FBC382FDE344621BECC943371ED2EA43133C36ABAAEBB198C8CCB0875`.
- Current reviewed baseline: `DF35C6D648DCF90F22FD5587F668C2ED29A780C733723D214CBADAD0ABF8AC3B` (1,507 protected methods).
- Runtime ABI remains `t2t-native-v3-20261024`.

The reviewed replacement retains all 1,497 logical protected method contracts, including the ten Base Game pool dependencies added to the previous 1,487-method release baseline. The exact generated referee predicate identity changes from `HasRefBlockShotTalent>b__175_0` to `b__177_0`; its argument, live-player gate and native talent-query predicate are preserved. No missing Harmony target is skipped. The profile keeps all 137 comparison sites, 109 native squad queries, 54 named hooks and 21 raw jersey callbacks. Comparison instruction indices, feature/literal/kind inventories and the native query inventory are unchanged.

Of the protected bodies, 1,093 retain the canonical hash. Native relocation comparison, exact field declarations, switch-table targets and a separate manual review cover the remaining changes. The inspected differences include native Forward/ForwardData field movement, generated ordinals, scheduling of independent stat loads/stores, native FirstBlood goal handling and prisoner boss ball ownership. The production verifier still requires the exact reviewed bodies; it does not automatically approve those semantic differences on future builds.

The fresh-run site relocates to `0x4436EA7`, instruction index 915. Its bytes `400FB6C60A4758`, SIL/RDI/R15 register contract, state layout and relative switch destinations remain unchanged. No emitter algorithm changes are required.

## Raw callbacks and generated interop

All 21 jersey callbacks were compared through their complete bodies, covering 7,229 instructions. Their changed anonymous dependencies were followed through 43 complete helper pairs. Twenty Forward field accesses move to the same named fields at their new offsets. Ten callback/flow state layouts remain unchanged. The Forward InitializeSkin coroutine and its closed generic runner change ordinal 155 to 158 without changing the reviewed native signature ABI or alias count. Named hook signatures remain unique and unchanged.

An independent generation using the installed provider assemblies, Unity base libraries and the loader's .NET 6.0.7 runtime produces 221 interop DLLs matching installed output outside the permitted Module MVID. Provider and Unity library hashes remain unchanged. The existing fingerprint algorithm is unchanged; the reviewed expected fingerprints are:

- MainScriptsAssembly: `534E62A45DF8758B4DF3877FAA8DD8D74EFD1389E3585701ABAFD9A4829CA2F6`.
- spine-csharp: `EBAB8BDBF5E3E3673DE25221D84C0900E1B91C8D8250B24D1A987379B89B4E81`.

The separately reviewed 33 custom reward native bodies retain their argument/register ABI and semantics; 22 addresses and 19 fixed-wrapper prologues relocate. Native FirstBlood addition/denial executes before the existing custom repeat/cancellation boundaries, so the adapter preserves the new native accounting order.

## Native gameplay changes and imported candidates

The publisher added `ForwardData.preventEliminatorRemoval`, moving its following stats by four bytes. Actual serialized stock assets contain this field on all 494 inspected forwards: 493 are false; only the nonstandard Prince asset is true. All 69 entries in the standard nine-player bench and thirty-player free-agent/superstar pools are false. Unbound imported Base Game candidates explicitly clear this donor-only flag; native-bound entries retain their native value. The standard pool ids, names, asset bindings and exported fields are unchanged.

Native GameStats now tracks whether its last goal received a FirstBlood bonus and updates denied-goal handling. Prisoner boss ball/chain setup now records successful holder/ball ownership and has right-chain attachment support. Those changes are in the publisher's GameAssembly. Static comparison does not establish whether any of them caused or fixed the separately reported Hockey FC freeze.

## Evidence limits

The staged profile was independently verified against the installed native pair and matched the generated runtime profile byte for byte. The native/interop review and isolated compilation establish their stated static contracts. Live menu, match and save acceptance remain separate checks owned by the release validation workflow.

Detailed input identities, full-body comparisons, raw callback ABI/layout evidence, preservation receipts, independent generation and verification logs are retained under `artifacts/compatibility-26372689-20261101`. Historical build receipts remain unchanged.

## Empty-star FullSequence continuation

The initial 1,497-method compatibility migration remains intact. The current pair adds ten narrowly reviewed native continuation/lifecycle dependencies for 1,507 methods total, with comparison/query/named/raw-hook inventories unchanged.

FullSequence chooses power-up, superstar, relic, then native random starters. The shipped step controller accepts PowerUp -> SuperStar -> Relic, with no direct PowerUp -> Relic transition. An owned empty-star result uses a menu-owned coroutine to let the current caller finish, cancel the former reveal and observe the actual native SuperStar state before starting the native relic stage. No placeholder is offered. The final native grant still resolves its Angus recipient; only the missing recruit insertion is suppressed. Ownership is retained through settings changes and cleared at completion, reinitialization or destruction. Native acceptance is recorded separately from the static/linked checks.
