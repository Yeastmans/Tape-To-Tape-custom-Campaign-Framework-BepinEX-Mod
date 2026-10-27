# Revision 30 reward separation

Custom CCF_Reward_* runtime dump entries are excluded from native catalogs regardless of whether their owning campaign is open. CustomRewardCatalog still supplies authored definitions to the Custom sections. Unknown IDs are not considered normal-run rewards, and copied Native Defaults can restrict native eligibility but cannot grant it.

Regression checks reproduce the custom catalog leak and a stale default selecting opponent-only Karlson behavior. They verify native talent selection, separate custom controls, preserved explicit IDs and saved definitions. The installed game TalentRepository was freshly decoded: all 158 native recruit base/upgrade outcomes match the existing list. GameAssembly SHA-256: 54982e5c98932b701b4549086bf08ecc792da5de43a5e3627f8804fe657aff58. data.unity3d SHA-256: 7cf8c90bf7af54b3d8ae3021da4081ac87f2d9c12213cc2848f21e962ccb49f0.

Open Rewards after updating: native categories must not list Oath of Maedhros or other custom runtime clones; authored custom talents remain in Custom Talents. Base game mode leaves unavailable/opponent-only entries unchecked. Explicit custom selection remains available. No live gameplay observation is claimed.
