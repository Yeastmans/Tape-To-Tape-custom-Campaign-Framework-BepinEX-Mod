# Goalie cache and card native value boundary repair

Parent: `1e522b26723cca7775742a0584747a87d81420cd`. Installed predecessor: `BCDAEAC7FB34C9148F7F3EADE48DD90EE8C5C6A379C8A4A52E93FA7B98F64E2C`. Game build remains 26141904 and compatibility baseline remains `89C2BF9BC714D598CF5A82FB112D34D191C7C4DE66F03ECB69CE5DF5D2E13627`.

## Observed load failure

The combined predecessor installed all 35 goalie hooks successfully, then faulted while loading the saved run. The new dump establishes a separate fault from the earlier double-detour crash: native virtual method lookup at GameAssembly+0x82D007 received metadata namespace text `ES3Types` as the object for `System.Type.get_FullName`.

`GoalieNativeCheckpoint` read `Dictionary<string,ES3Data>.TryGetValue`. In the shipped interop, ES3Data is a managed class derived from Il2CppSystem.ValueType, representing a native 16-byte struct containing type and byte-array references. The generic out wrapper tests managed `typeof(TValue).IsValueType`, allocates a pointer-sized local for this class, and then calls `PointerToValueGeneric<TValue>(firstPointer,false,false)`. The native out write requires 16 bytes. The conversion boxes the ES3Type object's header as an ES3Data payload. Its fake `entry.type` consequently points at the ES3Type class, and `entry.type.type` at offset 0x18 reads that class's namespace pointer.

Captured pointers confirm the full chain: valid ES3Type object `0x274A95EEBC0`, its class `0x270820AC1F0`, corrupted second ES3Type wrapper retaining that class, and namespace/fault argument `0x271A5F9D03C`. Evidence is preserved under `artifacts/feature-test-crash-20261022/combined-native-crash-review.md`, `combined-object-proof.json`, `combined-dump-details.txt`, and the original combined dump. No corrupted value is admitted or exception suppressed by this repair.

## Boundary changes

All three ES3Data reads now use one typed helper: `ContainsKey` followed by the generic indexer getter. The shipped getter receives the boxed native struct from `il2cpp_runtime_invoke` and passes `false,true` to PointerToValueGeneric; it does not interpret the first field as a value address. Existing file, type, array-pointer and exact-byte comparisons remain intact.

The generic indexer setter has the related input defect: a boxed header is passed without unboxing. Failed marker injection instead restores the retained entry through native `IDictionary.set_Item(Object,Object)`. Independent native inspection of RVA 0x36427A0 verifies the boxed-class check and 16-byte payload copy from object+0x10 before TryInsert. An absent prior marker still rolls back by removal. Neither path rewrites unrelated keys or synthesizes a replacement entry.

The same review found two `List<SkaterDataForUI>.Add` calls in goalie selection. SkaterDataForUI is a 48-byte native struct represented as a managed class. The generic Add wrapper passes its boxed header as payload. Both active and optional reserve cards now use `IList.Add(Object)`. Native RVA 0x1F48310 checks the box and copies three 16-byte chunks from offsets 0x10, 0x20 and 0x30 before native Add. Card order, constructor values and selection behavior remain unchanged. This removes a proven unsafe card boundary; the human GM flow still requires validation.

Reference-valued `ES3File.cachedFiles.TryGetValue` remains valid. Other goalie out calls are managed collections, Harmony state, or the explicit native exception pointer. Async-task builders use dedicated generated value-type methods; their wrapper representation does not pass through these generic collection input/out paths.

## Diagnostics and limits

`GoalieCacheInteropChecks` inspects the actual installed MainScriptsAssembly, Il2Cppmscorlib and Il2CppInterop.Runtime, plus the compiled production runtime. The initial predecessor run passed 11 wrapper checks and failed seven production boundary checks. The repaired build passes 19 checks, including a scan of all compiled goalie routes for wrapped-native-value generic collection input/out calls. These are IL/boundary diagnostics, not native execution.

Seven added source-linked assertions cover exact type/array ownership, missing entries, equal bytes with a different native array, failed readback restoring both prior references through the boxed boundary, unrelated-key preservation, and absent-marker removal. The complete related checkpoint/completion suite passes 411 checks; the goalie menu suite passes 130. Boundary doubles now represent card boxing explicitly. The existing actual-provider Harmony, wrapper-startup, native-scope and published-package diagnostics are repeated on the final candidate and recorded in its separate receipt.

No game launch, installation or real save mutation is performed by this task. Runtime and Studio are staged separately from every earlier candidate. The safe updater allowlist includes the installed BCDA predecessor. Human validation remains pending: Continue the same saved run, verify map load, enter the GM node, inspect/select active and reserve cards, cancel without changing the roster, then complete a recruitment and reload to check persistence. Match entry remains a separate human check after load succeeds.
