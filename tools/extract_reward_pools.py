"""Decide which Talents / Relics / Abilities can be offered as rewards in a normal Tape to Tape campaign run.

Reads only the installed game files. Evidence for the rule is the disassembly produced with
tools/reward_pool_disasm.py (Il2CppDumper output for the installed build lives in
artifacts/reward-pools/dump_build25541412; the older dump_2026-09-25_build25538794 does NOT match
the installed GameAssembly.dll - its RVAs are shifted).

Stage 1 (this file, `decode`): decode TalentRepository / RelicRepository / AbilityRepository /
UnlocksRepository / RunSquadScriptableObject and every Talent / Relic / Ability asset from
data.unity3d -> artifacts/reward-pools/decoded.json
Stage 2 (`classify`): apply the pool rule -> artifacts/reward-pools/normal_run_pool.tsv

Run with a Python that can import the bundled UnityPy (Python 3.12 runtime), e.g.
  <codex runtime>/python.exe tools/extract_reward_pools.py
"""
import hashlib, json, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/reward-pools'
META = OUT / 'dump_build25541412'
GAME = Path(r'C:\Steam\steamapps\common\Tape to Tape')
BUNDLE = GAME / 'Tape to Tape_Data/data.unity3d'
CATALOG = Path(r'C:\Users\kiran\AppData\Local\T2T Campaign Studio\T2T_Dumps\_gui_data\_reward_catalog.tsv')

# TalentCategory / RelicCategory flags (dump.cs enums)
TC = dict(Offensive=1, Defensive=2, Utility=4, Speed=8, Checking=16, Power=32, Accuracy=64, CampaignExclusive=128,
          UsedInCampaignPool=256, Butchery=512, Goalie=1024, AIBehaviour=2048, Timer=4096, MaxGoal=8192, Chaos=16384,
          Injury=32768, OnlyStats=65536, UsedInCampaignGoaliePool=131072, Fart=262144, Enrage=524288,
          Freeagents=1048576, Critical=2097152, SkaterCustomization=4194304, GoalieCustomization=8388608,
          SlapshotEffect=16777216, Passing=33554432, Helmet=67108864, PracticeMode=134217728)
RC = dict(Offensive=1, Defensive=2, Utility=4, Speed=8, Checking=16, Power=32, Accuracy=64, Chaos=128, Coach=256,
          OnlyStats=512, UsedInCampaignPool=1024, BossRelics=4096, GoalieRelics=8192, Timer=16384, MaxGoal=32768,
          Injury=65536, Fart=131072, Enrage=262144, Critical=524288, Customization=1048576, Referee=2097152,
          Passing=4194304, GoldenHelmet=8388608, Starter=16777216)
def flags(v, table): return '|'.join(k for k, b in table.items() if v & b) or 'None'

def sha(path):
    with path.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()

def decode():
    sys.path.insert(0, str(ROOT / 'artifacts/unity-asset-tools'))
    import UnityPy
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
    expected = {Path(v['Path']).name: v['Hash'].lower() for v in json.loads((META / 'source-hashes.json').read_text(encoding='utf-8-sig'))}
    for p in (GAME / 'GameAssembly.dll', GAME / 'Tape to Tape_Data/il2cpp_data/Metadata/global-metadata.dat', BUNDLE):
        assert sha(p) == expected[p.name], f'{p.name} does not match the dump in {META}'
    env = UnityPy.load(str(BUNDLE))
    gen = TypeTreeGenerator(next(iter(env.objects)).assets_file.unity_version)
    gen.load_local_dll_folder(str(META / 'DummyDll'))
    trees, index, items, special = {}, {}, {}, {}
    def ref(owner, v):
        if not v or not v.get('m_PathID'): return None
        fid = v['m_FileID']
        if fid == 0: name = owner.name
        elif 0 < fid <= len(owner.externals): name = Path(owner.externals[fid - 1].path).name
        else: name = f'ext{fid}'  # script references and other non-file ids
        return f'{name}:{v["m_PathID"]}'
    def read(obj, sc):
        key = (sc.m_AssemblyName, '.'.join(filter(None, [sc.m_Namespace, sc.m_ClassName])))
        if key not in trees:
            try: trees[key] = gen.get_nodes_up(*key)
            except Exception: trees[key] = None
        if trees[key] is None: return None
        try: return obj.read_typetree(trees[key])
        except Exception: return None
    wanted = {'TalentRepository', 'RelicRepository', 'AbilityRepository', 'UnlocksRepository', 'RunSquadScriptableObject'}
    for obj in env.objects:
        if obj.type.name != 'MonoBehaviour': continue
        h = obj.read(check_read=False)
        sc = h.m_Script.read() if h.m_Script else None
        if sc is None: continue
        key = f'{obj.assets_file.name}:{obj.path_id}'
        index[key] = (obj, sc)
        is_special = sc.m_ClassName in wanted or sc.m_ClassName.endswith('Unlock')
        d = read(obj, sc)
        if not isinstance(d, dict): continue
        if is_special:
            special[key] = (sc.m_ClassName, d, obj.assets_file)
        if 'id' in d and ('powerupName' in d or 'relicName' in d):
            kind = 'Relic' if 'relicName' in d else ('Ability' if 'maxCharges' in d else 'Talent')
            items[key] = dict(kind=kind, cls=sc.m_ClassName, ns=sc.m_Namespace, asset=d.get('m_Name'), id=d['id'],
                              level=d.get('level'), category=d.get('category'), displayInUI=d.get('displayInUI'),
                              isHidden=d.get('isHidden'), debugAppearMoreOften=d.get('debugAppearMoreOften'),
                              name=d.get('powerupName') or d.get('relicName'))
    def lists(key, fields=None):
        cls, d, owner = special[key]
        out = {}
        for f, v in d.items():
            if isinstance(v, list) and v and isinstance(v[0], dict) and 'm_PathID' in v[0]:
                out[f] = [ref(owner, x) for x in v]
            elif isinstance(v, dict) and set(v) == {'m_FileID', 'm_PathID'}:
                out[f] = ref(owner, v)
            elif f != 'm_Script' and not isinstance(v, (dict, list)):
                out[f] = v
        return cls, out
    repos, unlocks, squads = {}, {}, {}
    for key in special:
        cls, data = lists(key)
        if cls in ('TalentRepository', 'RelicRepository', 'AbilityRepository', 'UnlocksRepository'): repos[cls] = data
        elif cls == 'RunSquadScriptableObject': squads[data.get('m_Name')] = data
        else: unlocks[key] = dict(cls=cls, **{k: v for k, v in data.items() if k in ('reward', 'm_Name', 'unlockRequirement', 'timing', 'achievementKey')})
    doc = dict(bundle_sha256=sha(BUNDLE), dump=META.name, items=items, repos=repos, unlocks=unlocks, squads=squads)
    (OUT / 'decoded.json').write_text(json.dumps(doc, indent=1, ensure_ascii=False), encoding='utf-8')
    print(len(items), 'items;', {k: len(v) for k, v in repos.items()}, len(unlocks), 'unlocks', len(squads), 'squads')

# ---------------------------------------------------------------------------------------------
# Stage 2. The rule, from disassembly of installed build 25541412 (RVAs from dump_build25541412):
#  * Every random reward pick goes through TalentRepository.GetRandomTalents (0x369E4C0),
#    RelicRepository.GetRandomRelics (0x3660400) or AbilityRepository.GetRandomAbilities (0x3683E00).
#    Each reads ONLY the master list (talents @+0x20 / relics @+0x18 / abilities @+0x18), keeps
#    assets whose `category` shares a bit with the requested category (matchAnyCategory=true at every
#    normal-run call site), drops level != 1 (talents/relics; abilities: level == requested level, 1),
#    drops CampaignUtil.GetExcluded*Rewards (IsEligibleReward false, UnlockSystem.IsUnlocked false,
#    squad exclusions, GameRule Timer/MaxGoal), and with mustBeInPool also requires UsedInCampaignPool.
#    Picks are weighted (PowerUp/Relic.GetRewardWeight) and then TryUpgrade* swaps in the level+1 asset
#    of the same C# type (GetX(Type, level+1) = first match in the master list) with p = 2.5% / 7.5% act 2 /
#    15% act 3 (RunData._currentAct), skipped for relics with isHidden.
#  * Normal-run callers and the categories they request (all matchAnyCategory=true):
TALENT_SOURCES = {  # TalentCategory bit -> who offers it with mustBeInPool=false
    TC['UsedInCampaignPool']: 'post-match & talent-node rewards (RewardsStep.GenerateTalentRewards/LaunchTalentReward/'
                              'LaunchForcedSkaterTalentReward, TalentNodeReward), General Manager, free agents, bench bonus',
    TC['OnlyStats']: 'Stats squad (id statrewards) post-match/node rewards and StatRewardRelic extra pick (RogueUtils.GetRandomTalentRewards, mustBeInPool=false)',
    TC['Chaos']: 'Chaos event (ChaosEvent3Choice.Initialize, category Chaos, mustBeInPool=false)',
    TC['Butchery']: 'Nurse event (NurseEvent.Initialize, category Butchery, mustBeInPool=false)',
    TC['UsedInCampaignGoaliePool']: 'Fan #1 goalie-talent sub-event (GoalieTalentsSubEvent.Initialize, category UsedInCampaignGoaliePool, mustBeInPool=false)',
}
RELIC_SOURCES = {
    RC['UsedInCampaignPool']: 'post-match relic reward after normal/elite games (RewardsStep.LaunchRelicReward) and relic nodes (RelicNodeReward)',
    RC['BossRelics']: 'post-match reward after a boss game (LaunchRelicReward: BossBlueprint -> BossRelics) and mid-run Coach events',
    RC['Starter']: 'Coach event on the starting floor (CoachEvent.Initialize: currentFloor == 0 -> Starter)',
}
ABILITY_SOURCES = {
    TC['UsedInCampaignPool']: 'ability / Blademaster nodes (AbilityNodeReward, BlademasterNodeReward), Fan #1 events, random-ability relic/talent effects',
}
TIMER_MAXGOAL = {'Talents': (TC['Timer'], TC['MaxGoal']), 'Relics': (RC['Timer'], RC['MaxGoal']), 'Abilities': (TC['Timer'], TC['MaxGoal'])}

def classify():
    d = json.loads((OUT / 'decoded.json').read_text(encoding='utf-8'))
    items, repos = d['items'], d['repos']
    unlock_of = {}
    for u in d['unlocks'].values():
        r = u.get('reward')
        if r in items: unlock_of[items[r]['id']] = f"{u['cls']}"
    conf = {'Talents': ('TalentRepository', 'talents', TALENT_SOURCES, TC, 'goalieTalent'),
            'Relics': ('RelicRepository', 'relics', RELIC_SOURCES, RC, None),
            'Abilities': ('AbilityRepository', 'abilities', ABILITY_SOURCES, TC, None)}
    status = {}  # asset key -> (in_pool, reason)
    for group, (repo, field, sources, table, _) in conf.items():
        master = [k for k in repos[repo][field] if k in items]
        mask = 0
        for b in sources: mask |= b
        base_ok = {}
        for k in master:
            v = items[k]
            if v['level'] != 1: continue
            hit = [s for b, s in sources.items() if v['category'] & b]
            if hit:
                base_ok[k] = True
                note = '; '.join(hit)
                tm = TIMER_MAXGOAL[group]
                if v['category'] & tm[0]: note += '; Timer-mode only (excluded when GameRule=Maxgoal)'
                if v['category'] & tm[1]: note += '; Max-goal-mode only (excluded when GameRule=Timer)'
                if group == 'Talents' and v['category'] & TC['AIBehaviour']: note += '; AIBehaviour: dropped when more than 2 human players'
                if v['id'] in unlock_of: note += f"; gated by {unlock_of[v['id']]} (assumed unlocked)"
                status[k] = ('yes', f"level 1, category {flags(v['category'], table)} -> offered by {note}")
            else:
                status[k] = ('no', f"level 1 but category {flags(v['category'] or 0, table)} matches no normal-run reward request "
                                   f"(needs one of {flags(mask, table)})")
        # upgrades: GetX(Type, 2) = first level-2 asset of the same C# type in the master list
        first_l2 = {}
        for k in master:
            v = items[k]
            if v['level'] == 2: first_l2.setdefault((v['ns'], v['cls']), k)
        upgrade_from = {}
        for k in master:
            v = items[k]
            if v['level'] == 1:
                t = first_l2.get((v['ns'], v['cls']))
                if t: upgrade_from.setdefault(t, []).append(k)
        for k in master:
            v = items[k]
            if v['level'] != 2: continue
            bases = upgrade_from.get(k, [])
            offered = [b for b in bases if b in base_ok and not (group == 'Relics' and items[b].get('isHidden'))]
            if offered:
                status[k] = ('yes', 'level 2: upgrade of offered ' + ', '.join(repr(items[b]['asset']) for b in offered) +
                             f" ({ {'Talents': 'TryUpgradeTalent', 'Relics': 'TryUpgradeRelic', 'Abilities': 'TryUpgradeAbility'}[group]} on each rolled reward: 2.5%, 7.5% in act 2, 15% in act 3; also upgrade events)")
            elif bases:
                status[k] = ('no', 'level 2: its level-1 base ' + ', '.join(repr(items[b]['asset']) for b in bases) +
                             ' is never offered, so it cannot be rolled or upgraded from a reward')
            else:
                same = [items[x]['asset'] for x in first_l2.values() if x != k and (items[x]['ns'], items[x]['cls']) == (v['ns'], v['cls'])]
                why = f"another level-2 asset of type {v['cls']} is found first" if same or first_l2.get((v['ns'], v['cls'])) != k else f"no level-1 asset of type {v['cls']} in the list"
                status[k] = ('no', f'level 2 but unreachable: {why} (upgrade lookup is by C# type)')
        for k, v in items.items():
            if v['kind'] == group[:-1].replace('Abilitie', 'Ability') and k not in master and k not in status:
                status[k] = ('no', f'not in {repo}.{field} (the only list random rewards and upgrades read)')
    # write rows for every catalog id
    tal = repos['TalentRepository']
    goalie_list = {items[k]['id'] for k in tal.get('goalieTalent', []) if k in items}
    rows, notes = [], []
    by_id = {}
    for k, v in items.items(): by_id.setdefault(v['id'], []).append(k)
    for line in CATALOG.read_text(encoding='utf-8').splitlines():
        if not line or line.startswith('#'): continue
        c = line.split('\t'); kind, cid, clevel = c[0], c[2], c[3]
        keys = by_id.get(cid, [])
        if not keys:
            rows.append((kind, cid, c[4], clevel, 'no', 'id not found in shipped data.unity3d')); continue
        k = next((x for x in keys if x in status and status[x][0] == 'yes'), next((x for x in keys if x in status), keys[0]))
        v = items[k]
        ok, reason = status.get(k, ('no', 'not classified'))
        is_goalie = bool((v['category'] or 0) & (TC['UsedInCampaignGoaliePool'] | TC['Goalie'])) if v['kind'] != 'Relic' else False
        if kind == 'Goalie Talents' and not (v['id'] in goalie_list or is_goalie):
            notes.append(f"{cid}\t{v['asset']}\tcatalog says Goalie Talents but asset has no Goalie flags and is not in goalieTalent")
        if kind == 'Talents' and (v['id'] in goalie_list or (v['category'] or 0) & TC['UsedInCampaignGoaliePool']):
            notes.append(f"{cid}\t{v['asset']}\tcatalog says Talents but asset is a goalie talent (goalieTalent list / UsedInCampaignGoaliePool)")
        if str(v['level']) != clevel: notes.append(f"{cid}\t{v['asset']}\tcatalog level {clevel} != asset level {v['level']}")
        rows.append((kind, cid, v['asset'], str(v['level']), ok, reason))
    with (OUT / 'normal_run_pool.tsv').open('w', encoding='utf-8', newline='\n') as f:
        f.write('kind\tid\tasset_name\tlevel\tin_pool\treason\n')
        for r in rows: f.write('\t'.join(r) + '\n')
    (OUT / 'kind_mismatches.tsv').write_text('id\tasset\tissue\n' + '\n'.join(notes) + '\n', encoding='utf-8')
    from collections import Counter
    print(Counter((r[0], r[4]) for r in rows))
    print(len(notes), 'kind/level notes')

if __name__ == '__main__':
    if len(sys.argv) < 2 or sys.argv[1] == 'decode': decode()
    if len(sys.argv) < 2 or sys.argv[1] == 'classify': classify()
