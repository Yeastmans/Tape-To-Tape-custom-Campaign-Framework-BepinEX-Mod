# R43 Studio loading and signing validation

The Windows release is `beta-1.0-r43-build-1793910332792`. It builds on R42 commit `0fa3e50177dbf6b2014585825394d40b25fd3c33` and targets Tape to Tape Steam build 26372689.

## Studio loading

Fresh Studio processes rendered Home and a saved campaign through Avalonia with Skia drawing and routed pointer input. The R42 and R43 Windows packages used the same copy of a library containing eight campaign directories and 1,114 custom head directories on the same SSD. The operating system managed its file cache normally between runs.

| Time from first pointer action to the ready screen | R42 median | R43 median |
| --- | ---: | ---: |
| Home after choosing Continue | 7.471 s | 0.494 s |
| Yeastman's Wacky Crazy Funtime overview after choosing Open | 42.226 s | 0.358 s |

Each median covers three fresh processes per build. On the R43 package, the median time from releasing the button to visible progress was 76.6 ms for Home and 9.6 ms for the campaign. Separate runs held the background read open and captured the progress bars moving. The candidate's Home and overview screens matched the R42 reference images after loading. Checks also opened opponents and returned to the overview, and recovered from a bad startup path and a locked campaign descriptor.

The four product assemblies in the production Studio build contain the same 7,638 method bodies and 25 embedded resources as the previously timed candidate. Its earlier medians were 0.424 s for Home and 0.315 s for the campaign. The comparison also matched the method signatures, field and type references, and user strings used by those bodies.

## Signing

The source-linked recruitment checks exercised occupied and vacant skater slots, goalie replacement, cancellation, repeated input, legacy offers and deferred roster changes. The compatibility verifier checked 1,520 protected native methods. The compiled Harmony checks used the game's generated wrappers. A player still needs to try both the controller and mouse in a live recruitment visit.

## Focused checks

| Suite | Checks passed |
| --- | ---: |
| Native skater recruitment | 2001 |
| Goalie recruitment | 246 |
| Mod compatibility | 156 |
| Compiled Harmony interactions | 67 |
| Startup and Home | 13 |
| Campaign opening and saving | 18 |
| Deferred data and visible artwork | 8 |
| Head workflows | 117 |
| Roster reward names | 11 |
| Play Now exports | 97 |
| **Total** | **2734** |

## Windows package

The installer places the approved runtime in the Studio payload and the game's plugin directory. Package verification checks every extracted file, all six MapSeams PNGs in each destination, the R42 upgrade identity and the following fingerprints.

| Item | SHA-256 or ID |
| --- | --- |
| Runtime | `9FBDBA63EBA29E11CAB490199EDA184D79CC9BDB062937EE1CA5DA172665E315` |
| Embedded and external compatibility profile | `1E66E5474E5CEC3FBC2C5D47E0EE18506826D86D7EC457BD84CA38AA46CD44CF` |
| Studio native baseline file | `14430DAAE03A868383E32B8744D6116D2B6602C3C4B4674AA9F9DDB929D7CED9` |
| Native baseline ID | `9270A0E95B7DCEE1A1FD30EC3376DE7062483BFB7E9E0CD5E68D36591BD361D8` |
