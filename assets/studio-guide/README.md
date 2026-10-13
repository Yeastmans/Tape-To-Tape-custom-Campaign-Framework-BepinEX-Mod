# Studio guide illustrations

These PNGs are captured Avalonia editor views used by the read-only Studio guide. Captions and topic mappings are in `src/CampaignCreator/StudioGuideScreens.cs`; contextual instructions are in Page Help. Guide pictures illustrate controls and do not perform actions.

The approved illustrations contain no extracted game artwork or real user content. Game previews are unavailable in those captures, and the Head Editor examples use original vector smiley artwork. Keep that distinction when replacing pictures: extracted player-game caches do not belong in the application package.

When a layout changes, capture its current production editor view using content made for the illustration, with game artwork unavailable. Review the crop, text, selected tab, visible controls and caption together. Keep the existing filenames referenced by `StudioGuideScreens`, or update those references along with the PNGs. Do not trigger save/export/install actions on real user content to obtain a picture.

Build and package using [README_BUILD.md](../../README_BUILD.md). The shipping wrapper checks the published application's embedded pictures against approved product sources and rejects loose extracted artwork. After replacing an illustration, open its Studio guide topic in the resulting application to confirm that the image and caption still match the current controls. No retired capture harness is required by the source build.
