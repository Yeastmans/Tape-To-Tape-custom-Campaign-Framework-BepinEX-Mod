# Shared opponent scopes on native jersey hooks

The first-match access violation was recovered from the saved process dump at
`artifacts/feature-test-crash-20261022/match1-access-violation.dmp`. The original
exception context points to an invalid generated trampoline for goalkeeper skin
initialization. Its `mov r10, imm64` instruction was split by an appended jump at
offset six. The appended destination is the original callback plus ten bytes;
this does **not** establish that the decoder consumed only six bytes.

`JerseyLogoNativeHooks` already owned the native goalkeeper `MoveNext` entry.
`OpponentEffectScope.Install` subsequently requested a Harmony detour on that
same entry. The shipped provider follows existing jumps, and Harmony puts its
new trampoline into a cloned MethodInfo. The knockout uniform `MoveNext` had
the same duplicate ownership. This repair removes those second hook requests;
it does not change the detour provider or native ABI contracts.

## Ownership and behavior

- All 21 raw jersey entries are reserved even if installation is blocked.
- A shared route requires a unique stable consumer identity, exact resolved
  instance `void MoveNext()` signature, matching generated native entry address,
  and an actually installed raw owner.
- Reserved but unavailable, ambiguous, aliased, or unverifiable entries warn and
  remain unpatched by Harmony. Unrelated verified entries retain Harmony scopes.
- Successful detour disposal releases ownership. Failed disposal retains both
  the callback and ownership even while jersey appearance is disabled.
- The raw callback enters the existing opponent scope only around each original
  synchronous invocation. `finally` restores the prior scope on nested calls and
  exceptions. Capture, handoff, render application, and coroutine suspension keep
  their existing jersey behavior. Unregistered raw calls pass through unchanged.
- Startup records each shared route as `[Opponent] Shared native effect scope:`.
  The expected contracts are `2-move` and `7-move`.

## Verification

The initial production-installer regression reproduced both duplicate requests
before the repair. The completed harness resolves all 91 consumer methods from
the installed `MainScriptsAssembly` and `Assembly-CSharp` metadata. It executes
the production installer, ownership registry, and scope helper using a Harmony
request recorder. The final run supplies actual RVAs from the reviewed native
dump. No game methods or native detours execute in this harness.

All 14 checks pass: the two shared routes, 89 remaining Harmony requests,
blocked installation, partial rollback, compiler ordinal normalization, address
and identity rejection, idempotent registration, nested/repeated slices,
exception propagation with exactly one original invocation, pass-through, and
thread isolation. The shipped `Il2CppInteropUtils` resolver also finds native
MethodInfo fields for all 91 wrappers without reading their values or running
their generated type initializers.

The separate address audit compares all 91 native consumer bodies against 55
raw addresses: 21 jersey, 33 existing reward adapters, and the initial surface
adapter. Only `Goaltender.Goaltender.<InitializeSkin>d__91.MoveNext` (`2-move`)
and `KnockoutState.<ReapplySkin>d__115.MoveNext` (`7-move`) overlap. The authored
knockout consumer still names ordinal 105; stable normalization preserves that
consumer key for effect selection.

Runtime compilation passes with the existing unused-variable warning in
`Plugin.cs`. Local diagnostic outputs are `build/opponent-native-scope-checks.log`
and `build/opponent-native-ownership-audit.json`.

Run the address audit with the two preserved compatibility-review inputs:

```powershell
python tests/OpponentEffectNativeScopeChecks/audit_native_ownership.py <native-dump/script.json> <reward-address-map.json> build/opponent-native-ownership-audit.json
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/OpponentEffectNativeScopeChecks -- 'C:/Steam/steamapps/common/Tape to Tape' build/opponent-native-ownership-audit.json
```

Native gameplay acceptance remains pending. Fresh startup must show both shared
scope routes without unavailable warnings. Then the user's failing first-match
load, goalkeeper jersey-name selection, and knockout uniform restoration need
in-game confirmation. Startup, compilation, and these diagnostics do not prove
that match execution succeeds.
