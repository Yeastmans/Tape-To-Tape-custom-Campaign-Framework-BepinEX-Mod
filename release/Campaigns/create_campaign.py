#!/usr/bin/env python3
"""
Custom Campaign Creator for Tape to Tape
Walks you through creating a campaign.txt step by step.
"""
import os, sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CAMPAIGNS_DIR = SCRIPT_DIR  # Script lives in Campaigns/ folder

# === OPTION MAPS ===
BODY_SKINS = {
    "1": ("standard", "Standard (colorable - takes RGB)"),
    "2": ("tycoons", "Tycoons (business suit - fixed look)"),
    "3": ("princess", "Princess (armored - fixed look)"),
    "4": ("golfers", "Golfer (polo shirt - fixed look)"),
    "5": ("prisoners", "Prisoner (jumpsuit - fixed look)"),
    "6": ("mountaineers", "Mountaineer (fixed look)"),
    "7": ("hockey fc", "Hockey FC (soccer jersey - fixed look)"),
    "8": ("figure skaters", "Figure Skater (fixed look)"),
    "9": ("referee", "Referee (fixed look)"),
    "10": ("random body", "Random each game"),
}
HELMET_SKINS = {
    "1": ("team colors", "Team Colors (colorable - you set the RGB color next)"),
    "2": ("cage", "Face Cage (fixed look - no color needed)"),
    "3": ("random helmet", "Random each game"),
}
STICK_SKINS = {
    "1": ("black", "Black (fixed)"),
    "2": ("gold", "Gold (fixed)"),
    "3": ("red", "Red (fixed)"),
    "4": ("purple", "Purple (fixed)"),
    "5": ("teal", "Teal/Blue-Green (fixed)"),
    "6": ("red gold", "Red Gold (fixed)"),
    "7": ("sword", "Sword (fixed)"),
    "8": ("golf", "Golf Iron (fixed)"),
    "9": ("team stick", "Team Stick (colorable - takes RGB)"),
    "10": ("random stick", "Random each game"),
}
SKATE_SKINS = {
    "1": ("standard", "Standard (colorable - 3 colors: body, blade, laces)"),
    "2": ("random skates", "Random each game"),
}
SIZE_OPTIONS = {
    "1": ("ExtraSmall", "Extra Small - tiny, very fast, fragile"),
    "2": ("Small", "Small - quick, agile"),
    "3": ("Medium", "Medium - balanced"),
    "4": ("Big", "Big - strong checks, slower"),
    "5": ("ExtraBig", "Extra Big - powerful, slow"),
    "6": ("ExtraExtraBig", "Extra Extra Big - massive"),
    "7": ("random", "Random each game"),
}
FACE_EXAMPLES = [
    "Wiener", "Haggis", "Jerky", "Crockett", "Angus", "Rory", "Grohl",
    "Captain", "Poule", "Gratz", "Brie", "Amber", "Mental", "Krupp",
    "Joan", "Dalton", "Prince", "Lancelov", "Nasher", "Onepunch",
    "Helmet_Face", "random"
]
COLORABLE_SKINS = {"standard", "team colors", "team stick"}

REF_FILE = "VALID_VALUES.txt"

def ask(prompt, default=""):
    val = input(f"  {prompt}" + (f" [{default}]" if default else "") + ": ").strip()
    return val if val else default

def ask_yn(prompt, default="yes"):
    val = ask(prompt + " (yes/no)", default).lower()
    return val in ("yes", "y", "true", "1")

def ask_int(prompt, default=50):
    val = ask(prompt, str(default))
    try: return int(val)
    except: return default

def ask_rgb(prompt):
    """Ask for an RGB color with clear format help."""
    val = ask(prompt + "  [R,G,B | random | Enter=skip]")
    if not val: return None
    return val

def ask_rgb_3(label, c1="Primary", c2="Secondary", c3="Tertiary"):
    """Ask for 3 color channels."""
    print(f"    {label} (3 color channels, R,G,B each | random | Enter=skip):")
    v1 = ask(f"    {c1}")
    v2 = ask(f"    {c2}")
    v3 = ask(f"    {c3}")
    return v1 or None, v2 or None, v3 or None

def pick_option(prompt, options):
    print(f"\n  {prompt}")
    for k, (val, desc) in options.items():
        print(f"    {k}. {desc}")
    choice = ask("Pick number", "1")
    return options[choice][0] if choice in options else options["1"][0]

def pick_uniform(prompt, options, color_label):
    """Pick a skin, then ask RGB if it's colorable."""
    skin = pick_option(prompt, options)
    if skin in COLORABLE_SKINS:
        print(f"    '{skin}' is colorable. See {REF_FILE} Section 8 for format.")
        c = ask(f"    {color_label} color [R,G,B | random | Enter=skip]")
        return skin, c if c else None
    else:
        pass
    return skin, None

def pick_face():
    print(f"\n  See {REF_FILE} Section 7 for all faces.")
    return ask("Face", "random")

# === PLAYER CREATION ===

def create_player(position, is_detailed=True, is_line2=False):
    """Create a player section."""
    label = f"Line 2 {position}" if is_line2 else position
    print(f"\n  {'='*40}")
    print(f"  --- {label} ---")
    print(f"  {'='*40}")

    mode = ask("Import player or build manual? (import/manual/skip)", "manual")
    if mode.lower() == "skip":
        return None

    lines = [f"--- {label} ---\n"]

    if mode.lower().startswith("i"):
        print(f"    See {REF_FILE} Section 20 for import fields.")
        name = ask("Player name to import (or 'random')")
        lines.append(f"Import Player           = {name}\n")
        override_name = ask("Override display name? (Enter=keep imported name)")
        if override_name:
            lines.append(f"Name                    = {override_name}\n")
        if ask_yn("Override stats?", "no"):
            print(f"    See {REF_FILE} Section 11 for stat ranges.")
            lines.append(f"Speed                   = {ask('Speed')}\n")
            lines.append(f"Shot Power              = {ask('Shot Power')}\n")
            lines.append(f"Accuracy                = {ask('Accuracy')}\n")
            lines.append(f"Checking                = {ask('Checking')}\n")
        return lines

    # === Manual player ===
    name = ask("Player name (First Last)", "Player")
    lines.append(f"Name                    = {name}\n")
    lines.append(f"Number                  = {ask('Jersey number (1-99)', '88')}\n")
    lines.append(f"Face                    = {pick_face()}\n")
    lines.append(f"Left Handed             = {ask('Left handed? (yes/no/random)', 'no')}\n")
    lines.append(f"Skin Color              = {ask('Skin tone (light/dark/random)', 'light')}\n")

    size = pick_option("Player size:", SIZE_OPTIONS)
    lines.append(f"Size                    = {size}\n")

    # Stats
    print(f"\n    See {REF_FILE} Section 11 for stat ranges.")
    lines.append(f"Speed                   = {ask('Speed', '50')}\n")
    lines.append(f"Shot Power              = {ask('Shot Power', '50')}\n")
    lines.append(f"Accuracy                = {ask('Accuracy', '50')}\n")
    lines.append(f"Checking                = {ask('Checking', '50')}\n")

    # Ability
    print(f"\n    See {REF_FILE} Section 13 for abilities.")
    ability = ask("Ability")
    if ability:
        lines.append(f"Ability                 = {ability}\n")

    # Talents
    print(f"\n    See {REF_FILE} Section 14 for talents. Comma-separated.")
    talents = ask("Talents")
    if talents:
        lines.append(f"Talents                 = {talents}\n")

    # Random talents
    rand_count = ask("Random talents per game (0=none)", "0")
    if rand_count != "0":
        lines.append(f"Random Talents          = {rand_count}\n")
        print(f"    Which talents to pick from?")
        print(f"      'all'  = any talent in the game")
        print(f"      list   = specific talents, comma-separated")
        print(f"      Example: Onepunch, Crit Boost, Enraged")
        print(f"    See {REF_FILE} Section 14 for all talent names.")
        pool = ask("Random pool", "all")
        lines.append(f"Random Pool             = {pool}\n")

    if is_detailed:
        # Per-player uniform
        if ask_yn("Customize this player's uniform? (overrides team defaults)", "no"):
            stick, stick_c = pick_uniform("Stick:", STICK_SKINS, "Stick")
            lines.append(f"Stick                   = {stick}\n")
            helmet, helmet_c = pick_uniform("Helmet:", HELMET_SKINS, "Helmet")
            lines.append(f"Helmet                  = {helmet}\n")
            body, body_c = pick_uniform("Body:", BODY_SKINS, "Body")
            lines.append(f"Body                    = {body}\n")
            skates = pick_option("Skates:", SKATE_SKINS)
            lines.append(f"Skates                  = {skates}\n")
            if body_c: lines.append(f"Jersey Color            = {body_c}\n")
            if helmet_c: lines.append(f"Helmet Color            = {helmet_c}\n")
            if stick_c: lines.append(f"Stick Color             = {stick_c}\n")
            if skates == "standard":
                print(f"    Skates have 3 colors: body, blade, laces")
                sc = ask_rgb("Skate Body color")
                if sc: lines.append(f"Skates Color            = {sc}\n")
                bc = ask_rgb("Blade color")
                if bc: lines.append(f"Blade Color             = {bc}\n")
                lc = ask_rgb("Laces color")
                if lc: lines.append(f"Laces Color             = {lc}\n")
            elif skates == "random skates":
                lines.append(f"Skates Color            = random\n")
                lines.append(f"Blade Color             = random\n")
                lines.append(f"Laces Color             = random\n")
            gc = ask_rgb("Gloves color")
            if gc: lines.append(f"Gloves                  = {gc}\n")
            pc = ask_rgb("Pants color")
            if pc: lines.append(f"Pants                   = {pc}\n")
            bc = ask_rgb("Bicep color")
            if bc: lines.append(f"Bicep                   = {bc}\n")

        # Per-player colors
        if ask_yn("Customize this player's colors? (overrides team colors)", "no"):
            print(f"    See {REF_FILE} Section 8 for color format, Section 10 for fields.")
            c = ask_rgb("Jersey Color")
            if c: lines.append(f"Jersey Color            = {c}\n")
            c = ask_rgb("Gloves Color")
            if c: lines.append(f"Gloves Color            = {c}\n")
            c = ask_rgb("Helmet Color")
            if c: lines.append(f"Helmet Color            = {c}\n")
            c = ask_rgb("Pants Color")
            if c: lines.append(f"Pants Color             = {c}\n")
            c = ask_rgb("Skates Color (body)")
            if c: lines.append(f"Skates Color            = {c}\n")
            c = ask_rgb("Blade Color")
            if c: lines.append(f"Blade Color             = {c}\n")
            c = ask_rgb("Laces Color")
            if c: lines.append(f"Laces Color             = {c}\n")
            c = ask_rgb("Bicep Color")
            if c: lines.append(f"Bicep Color             = {c}\n")
            c = ask_rgb("Number Color")
            if c: lines.append(f"Number Color            = {c}\n")

    return lines


def create_goalie():
    """Create a goalie section."""
    print(f"\n  {'='*40}")
    print(f"  --- Goalie ---")
    print(f"  {'='*40}")

    mode = ask("Import goalie or build manual? (import/manual)", "manual")
    lines = ["--- Goalie ---\n"]

    if mode.lower().startswith("i"):
        name = ask("Goalie name to import (or 'random')")
        lines.append(f"Import Player           = {name}\n")
        override = ask("Override display name? (Enter=keep)")
        if override:
            lines.append(f"Name                    = {override}\n")
        return lines

    # Manual goalie
    lines.append(f"Name                    = {ask('Goalie name', 'Goalie')}\n")
    lines.append(f"Face                    = {pick_face()}\n")

    print(f"\n    See {REF_FILE} Section 12 for goalie stat ranges.")
    lines.append(f"Skill                   = {ask('Skill (overall modifier)', '50')}\n")
    lines.append(f"Catching                = {ask('Catching', '50')}\n")
    lines.append(f"Glove                   = {ask('Glove (glove-side saves)', '50')}\n")
    lines.append(f"Blocker                 = {ask('Blocker (blocker-side saves)', '50')}\n")
    lines.append(f"Five Hole               = {ask('Five Hole', '50')}\n")
    lines.append(f"Standing Speed          = {ask('Standing Speed', '50')}\n")
    lines.append(f"Butterfly Speed         = {ask('Butterfly Speed', '50')}\n")
    lines.append(f"Control                 = {ask('Control (rebound control)', '50')}\n")
    lines.append(f"Recovery                = {ask('Recovery', '50')}\n")
    lines.append(f"Pass Power              = {ask('Pass Power', '50')}\n")
    lines.append(f"Shot Power              = {ask('Shot Power', '50')}\n")
    lines.append(f"Poke Check              = {ask('Poke Check', '50')}\n")
    lines.append(f"Depth                   = {ask('Depth (positioning)', '50')}\n")
    lines.append(f"Pass Read               = {ask('Pass Read (0.0-1.0, higher=better)', '0.5')}\n")

    print(f"\n    See {REF_FILE} Section 15 for goalie talents. Comma-separated.")
    talents = ask("Goalie Talents")
    if talents:
        lines.append(f"Goalie Talents          = {talents}\n")

    return lines


def create_team(team_num, is_boss=False, is_final=False, act_num=0):
    """Create a full team section."""
    label = f"TEAM {team_num}"
    if is_final: label += " — FINAL BOSS"
    elif is_boss: label += " — BOSS"

    print(f"\n{'#'*50}")
    print(f"  {label} (Act {act_num})")
    print(f"{'#'*50}")

    lines = []
    lines.append(f"########################################\n")
    lines.append(f"##                                    ##\n")
    lines.append(f"##   {label:<36s}##\n")
    lines.append(f"##                                    ##\n")
    lines.append(f"########################################\n\n")

    # Setup method
    print("\n  How to set up this team?")
    print("    1. Import an in-game team (easiest)")
    print("    2. Build manually (full control)")
    print("    3. Mirror match (clone player's own team)")
    print("    4. Random team (different each launch)")
    method = ask("Pick 1-4", "1")

    if method == "3":
        lines.append(f"Import Team             = PLAYER\n")
        lines.append(f"Team Name               = {ask('Display name', 'Mirror Match')}\n\n")
        return lines

    if method == "4":
        lines.append(f"Import Team             = random\n")
        lines.append(f"Stat Scale              = {ask('Stat Scale (1.0=normal, 2.0=double)', '1.0')}\n")
        rt = ask("Random talents for every player (0=none)", "0")
        if rt != "0":
            lines.append(f"Team Random Talents     = {rt}\n")
            print(f"    Which talents to pick from?")
            print(f"      'all'  = any talent in the game")
            print(f"      list   = specific talents, comma-separated")
            print(f"      Example: Onepunch, Crit Boost, Enraged")
            print(f"    See {REF_FILE} Section 14 for all talent names.")
            lines.append(f"Team Random Pool        = {ask('Random pool', 'all')}\n")
        relics = ask(f"Relics (comma-separated, see {REF_FILE} Section 16, Enter=none)")
        if relics:
            lines.append(f"\n--- Team Relics ---\n")
            for r in relics.split(","):
                r = r.strip()
                if r: lines.append(f"{r}\n")
        lines.append(f"\n")
        return lines

    if method == "1":
        print(f"\n    See {REF_FILE} Section 17 for all team names.")
        print("    Works with in-game teams (Vancouver, Chicago, etc.)")
        print("    AND custom teams you've made in the team creator.")
        print("    Type the team name exactly as it appears in-game.")
        print("    Type 'random' for a random team each launch.")
        team_import = ask("Team name to import")
        lines.append(f"Import Team             = {team_import}\n")
        lines.append(f"Team Name               = {ask('Display name', team_import)}\n")
        lines.append(f"Stat Scale              = {ask('Stat Scale (1.0=normal)', '1.0')}\n")

        # Random talents for imported team
        rt = ask("Random talents for every player (0=none)", "0")
        if rt != "0":
            lines.append(f"Team Random Talents     = {rt}\n")
            print(f"    Which talents to pick from?")
            print(f"      'all'  = any talent in the game")
            print(f"      list   = specific talents, comma-separated")
            print(f"      Example: Onepunch, Crit Boost, Enraged")
            print(f"    See {REF_FILE} Section 14 for all talent names.")
            lines.append(f"Team Random Pool        = {ask('Random pool', 'all')}\n")

        # Relics
        print(f"    See {REF_FILE} Section 16 for relic names.")
        relics = ask("Relics (comma-separated, Enter=none)")
        if relics:
            lines.append(f"\n--- Team Relics ---\n")
            for r in relics.split(","):
                r = r.strip()
                if r: lines.append(f"{r}\n")

        # Bench size
        bench = ask("Bench size (0-10, Enter=default)", "")
        if bench:
            lines.append(f"Bench Size              = {bench}\n")

        lines.append(f"\n")
        return lines

    # === MANUAL TEAM ===
    lines.append(f"Team Name               = {ask('Team name')}\n")
    lines.append(f"City                    = {ask('City')}\n")
    lines.append(f"Abbreviation            = {ask('Abbreviation (3 letters)')}\n")
    print(f"    See {REF_FILE} Section 17 for team names.")
    logo = ask("Logo From")
    if logo:
        lines.append(f"Logo From               = {logo}\n")

    # === TEAM COLORS ===
    print(f"\n  ---- TEAM COLORS ----")
    print(f"    See {REF_FILE} Section 8 for color format, Section 9 for fields.")
    lines.append(f"\n--- Team Colors ---\n")

    # Jersey (3)
    print("  JERSEY (3 colors):")
    c = ask_rgb("  Jersey Primary (main body)")
    if c: lines.append(f"Jersey Primary          = {c}\n")
    c = ask_rgb("  Jersey Secondary (trim)")
    if c: lines.append(f"Jersey Secondary        = {c}\n")
    c = ask_rgb("  Jersey Accent (detail)")
    if c: lines.append(f"Jersey Accent           = {c}\n")

    # Away
    if ask_yn("Set away jersey colors? (Enter=mirror home)", "no"):
        print("  AWAY JERSEY (3 colors):")
        c = ask_rgb("  Away Primary")
        if c: lines.append(f"Away Primary            = {c}\n")
        c = ask_rgb("  Away Secondary")
        if c: lines.append(f"Away Secondary          = {c}\n")
        c = ask_rgb("  Away Accent")
        if c: lines.append(f"Away Accent             = {c}\n")

    # Equipment colors
    if ask_yn("Set equipment colors? (helmet, gloves, pants, skates, socks)", "no"):
        # Helmet
        c1, c2, c3 = ask_rgb_3("HELMET")
        if c1: lines.append(f"Helmet Color            = {c1}\n")
        if c2: lines.append(f"Helmet Secondary Color  = {c2}\n")
        if c3: lines.append(f"Helmet Tertiary Color   = {c3}\n")
        # Gloves
        c1, c2, c3 = ask_rgb_3("GLOVES")
        if c1: lines.append(f"Gloves Color            = {c1}\n")
        if c2: lines.append(f"Gloves Secondary Color  = {c2}\n")
        if c3: lines.append(f"Gloves Tertiary Color   = {c3}\n")
        # Pants
        c1, c2, c3 = ask_rgb_3("PANTS")
        if c1: lines.append(f"Pants Color             = {c1}\n")
        if c2: lines.append(f"Pants Secondary Color   = {c2}\n")
        if c3: lines.append(f"Pants Tertiary Color    = {c3}\n")
        # Skates
        c1, c2, c3 = ask_rgb_3("SKATES", "Skate Body", "Blade", "Laces")
        if c1: lines.append(f"Skates Color            = {c1}\n")
        if c2: lines.append(f"Blade Color             = {c2}\n")
        if c3: lines.append(f"Laces Color             = {c3}\n")
        # Socks
        c1, c2, c3 = ask_rgb_3("SOCKS")
        if c1: lines.append(f"Socks Color             = {c1}\n")
        if c2: lines.append(f"Socks Secondary Color   = {c2}\n")
        if c3: lines.append(f"Socks Tertiary Color    = {c3}\n")
        # Other
        c = ask_rgb("Bicep Color")
        if c: lines.append(f"Bicep Color             = {c}\n")
        c = ask_rgb("Number Color")
        if c: lines.append(f"Number Color            = {c}\n")

    # Transition
    if ask_yn("Set transition colors? (screen wipe effect)", "no"):
        c = ask_rgb("Transition Primary")
        if c: lines.append(f"Transition Primary      = {c}\n")
        c = ask_rgb("Transition Secondary")
        if c: lines.append(f"Transition Secondary    = {c}\n")
        c = ask_rgb("Transition Tertiary")
        if c: lines.append(f"Transition Tertiary     = {c}\n")

    # === TEAM UNIFORM ===
    print(f"\n  ---- TEAM UNIFORM ----")
    print(f"    See {REF_FILE} Sections 1-5 for skin options.")
    lines.append(f"\n--- Team Uniform ---\n")

    body, body_c = pick_uniform("Body:", BODY_SKINS, "Body")
    lines.append(f"Body                    = {body}\n")
    if ask_yn("Set Body Away separately?", "no"):
        body_a, _ = pick_uniform("Body Away:", BODY_SKINS, "Body Away")
        lines.append(f"Body Away               = {body_a}\n")

    helmet, helmet_c = pick_uniform("Helmet:", HELMET_SKINS, "Helmet")
    lines.append(f"Helmet                  = {helmet}\n")
    stick, stick_c = pick_uniform("Stick:", STICK_SKINS, "Stick")
    lines.append(f"Stick                   = {stick}\n")
    skates = pick_option("Skates:", SKATE_SKINS)
    lines.append(f"Skates                  = {skates}\n")
    if skates == "standard":
        print(f"\n    Skates have 3 color channels. Set each one:")
        print(f"    Format: R,G,B (e.g. 50,50,50) | 'random' | Enter=skip")
        sc = ask(f"    Skate Body color")
        bc = ask(f"    Blade color")
        lc = ask(f"    Laces color")
    elif skates == "random skates":
        print(f"    Random skates — assigning random colors to all 3 channels.")
        sc, bc, lc = "random", "random", "random"
    else:
        sc, bc, lc = None, None, None

    # Colors from uniform picks
    if body_c: lines.append(f"Jersey Primary          = {body_c}\n")
    if helmet_c: lines.append(f"Helmet Color            = {helmet_c}\n")
    if stick_c: lines.append(f"Stick Color             = {stick_c}\n")
    if sc: lines.append(f"Skates Color            = {sc}\n")
    if bc: lines.append(f"Blade Color             = {bc}\n")
    if lc: lines.append(f"Laces Color             = {lc}\n")

    # Gloves, Pants, Bicep — just ask RGB (mod auto-sets standard)
    gloves_c = ask_rgb("Gloves color")
    if gloves_c: lines.append(f"Gloves                  = {gloves_c}\n")
    pants_c = ask_rgb("Pants color")
    if pants_c: lines.append(f"Pants                   = {pants_c}\n")
    bicep_c = ask_rgb("Bicep color")
    if bicep_c: lines.append(f"Bicep                   = {bicep_c}\n")

    # Team random talents
    rt = ask("\n  Random talents for EVERY player on this team (0=none)", "0")
    if rt != "0":
        lines.append(f"Team Random Talents     = {rt}\n")
        print(f"    Which talents to pick from?")
        print(f"      'all'  = any talent in the game")
        print(f"      list   = specific talents, comma-separated")
        print(f"      Example: Onepunch, Crit Boost, Enraged")
        print(f"    See {REF_FILE} Section 14 for all talent names.")
        lines.append(f"Team Random Pool        = {ask('Random pool', 'all')}\n")

    # Relics
    print(f"\n    See {REF_FILE} Section 16 for relic names.")
    relics = ask("Relics (Enter=none)")
    if relics:
        lines.append(f"\n--- Team Relics ---\n")
        for r in relics.split(","):
            r = r.strip()
            if r: lines.append(f"{r}\n")
    else:
        lines.append(f"\n--- Team Relics ---\n")

    # Goalie
    goalie_lines = create_goalie()
    lines.append(f"\n")
    lines.extend(goalie_lines)

    # Players
    detailed = ask_yn("\n  Detailed player customization (uniform/colors per player)?", "no")
    for pos in ["Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense"]:
        player_lines = create_player(pos, is_detailed=detailed)
        if player_lines:
            lines.append(f"\n")
            lines.extend(player_lines)

    # Line 2
    print(f"\n  ---- LINE 2 (5 extra players) ----")
    if is_boss and act_num == 3:
        print("  RECOMMENDED: This is a FINAL BOSS team.")
        print("  Tycoons and some boss teams use 10-player rosters.")
        print("  Add a second line so the team has a full 10 players.")
    elif is_boss:
        print("  This is a BOSS team. Some boss teams (like Tycoons) use")
        print("  10-player rosters. Add Line 2 if this team needs it.")
    else:
        print("  Line 2 adds 5 extra players for a 10-player roster.")
        print("  Usually not needed for elite teams unless you want")
        print("  Tycoons or extra depth for immersion.")
    if ask_yn("Add Line 2 players?", "yes" if (is_boss and act_num == 3) else "no"):
        for pos in ["Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense"]:
            player_lines = create_player(pos, is_detailed=False, is_line2=True)
            if player_lines:
                lines.append(f"\n")
                lines.extend(player_lines)

    lines.append(f"\n\n")
    return lines


def parse_campaign_file(filepath):
    """Parse an existing campaign.txt into header (settings) and team blocks."""
    with open(filepath, "r") as f:
        content = f.read()

    lines = content.split("\n")

    # Find team boundaries — teams start with ########
    # Pattern: block of ###### lines containing "TEAM N"
    team_starts = []
    header_end = 0
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        # Detect team header block: starts with ######
        if line.startswith("####") and "TEAM" in "".join(lines[i:min(i+5, len(lines))]).upper():
            team_starts.append(i)
            if not team_starts or len(team_starts) == 1:
                header_end = i
        i += 1

    if not team_starts:
        return content, [], lines

    header_end = team_starts[0]
    header = "\n".join(lines[:header_end])

    # Extract team blocks
    teams = []
    for idx, start in enumerate(team_starts):
        end = team_starts[idx + 1] if idx + 1 < len(team_starts) else len(lines)
        block = "\n".join(lines[start:end])
        # Extract team name/info from the block
        team_name = f"Team {idx + 1}"
        for bline in lines[start:end]:
            stripped = bline.strip()
            if stripped.lower().startswith("team name") and "=" in stripped:
                team_name = stripped.split("=", 1)[1].strip()
                break
            elif stripped.lower().startswith("import team") and "=" in stripped:
                team_name = f"Import: {stripped.split('=', 1)[1].strip()}"
                break
        # Check if boss
        is_boss = "BOSS" in block[:300].upper()
        label = f"Team {idx+1}: {team_name}"
        if is_boss:
            label += " [BOSS]"
        teams.append({"label": label, "start": start, "end": end, "block": block, "index": idx + 1})

    return header, teams, lines


def edit_campaign():
    """Edit an existing campaign."""
    print("=" * 50)
    print("   EDIT EXISTING CAMPAIGN")
    print("=" * 50)
    print()

    # List available campaigns (subfolders with campaign.txt)
    campaigns = []
    for name in sorted(os.listdir(CAMPAIGNS_DIR)):
        folder = os.path.join(CAMPAIGNS_DIR, name)
        if os.path.isdir(folder) and os.path.isfile(os.path.join(folder, "campaign.txt")):
            campaigns.append(name)

    if not campaigns:
        print("  No campaigns found! Create one first.")
        return

    print("  Available campaigns:")
    for i, name in enumerate(campaigns, 1):
        print(f"    {i}. {name}")

    choice = ask("Pick campaign number", "1")
    try:
        idx = int(choice) - 1
        if idx < 0 or idx >= len(campaigns):
            raise ValueError()
    except:
        print("  Invalid choice.")
        return

    campaign_name = campaigns[idx]
    filepath = os.path.join(CAMPAIGNS_DIR, campaign_name, "campaign.txt")
    header, teams, all_lines = parse_campaign_file(filepath)

    if not teams:
        print("  Could not parse any teams from this campaign.")
        return

    while True:
        print(f"\n  Campaign: {campaign_name}")
        print(f"  {len(teams)} teams found:\n")
        for t in teams:
            print(f"    {t['label']}")

        print(f"\n  Options:")
        print(f"    1-{len(teams)}  = Edit a specific team (rebuilds it from scratch)")
        print(f"    s       = Edit campaign settings (act sequence, toggles)")
        print(f"    d       = Done (save & exit)")
        print(f"    q       = Quit without saving")

        action = ask("Action").strip().lower()

        if action == "q":
            print("  Cancelled — no changes saved.")
            return
        if action == "d":
            break
        if action == "s":
            print(f"\n  Current settings header:")
            for hl in header.split("\n"):
                if hl.strip() and not hl.strip().startswith("===="):
                    print(f"    {hl.rstrip()}")
            print()
            new_header_lines = []
            # Re-ask campaign settings
            act_seq = ask("Act Sequence (Enter=keep current)")
            replace_ch = ask("Replace Challenges (yes/no/Enter=keep)")
            replace_soccer = ask("Replace Soccer Ball (yes/no/Enter=keep)")
            replace_golf = ask("Replace Golf Ball (yes/no/Enter=keep)")

            # Rebuild header by modifying existing values
            for hl in header.split("\n"):
                stripped = hl.strip().lower()
                if act_seq and stripped.startswith("act sequence"):
                    new_header_lines.append(f"Act Sequence            = {act_seq}")
                elif replace_ch and stripped.startswith("replace challenges"):
                    new_header_lines.append(f"Replace Challenges      = {replace_ch}")
                elif replace_soccer and stripped.startswith("replace soccer"):
                    new_header_lines.append(f"Replace Soccer Ball     = {replace_soccer}")
                elif replace_golf and stripped.startswith("replace golf"):
                    new_header_lines.append(f"Replace Golf Ball       = {replace_golf}")
                else:
                    new_header_lines.append(hl)
            header = "\n".join(new_header_lines)
            print("  Settings updated!")
            continue

        # Team number
        try:
            team_num = int(action)
            if team_num < 1 or team_num > len(teams):
                raise ValueError()
        except:
            print("  Invalid choice.")
            continue

        t = teams[team_num - 1]
        print(f"\n  Rebuilding {t['label']}...")
        is_boss = "BOSS" in t["label"].upper()
        is_final = team_num == len(teams)
        new_team_lines = create_team(team_num, is_boss=is_boss, is_final=is_final, act_num=3 if is_final else 0)
        t["block"] = "".join(new_team_lines)
        t["label"] = f"Team {team_num}: (rebuilt)"
        # Re-extract name from new block
        for bline in t["block"].split("\n"):
            stripped = bline.strip()
            if stripped.lower().startswith("team name") and "=" in stripped:
                t["label"] = f"Team {team_num}: {stripped.split('=', 1)[1].strip()}"
                break
            elif stripped.lower().startswith("import team") and "=" in stripped:
                t["label"] = f"Team {team_num}: Import: {stripped.split('=', 1)[1].strip()}"
                break
        print(f"  Team {team_num} rebuilt!")

    # Save
    output = header
    if not output.endswith("\n\n"):
        output = output.rstrip("\n") + "\n\n"
    for t in teams:
        block = t["block"]
        if not block.endswith("\n"):
            block += "\n"
        output += block

    with open(filepath, "w") as f:
        f.write(output)

    print(f"\n{'='*50}")
    print(f"  SAVED!")
    print(f"  File: {filepath}")
    print(f"{'='*50}")


def main():
    print("=" * 50)
    print("   CUSTOM CAMPAIGN CREATOR")
    print("=" * 50)
    print()
    print("  1. Create new campaign")
    print("  2. Edit existing campaign")
    choice = ask("\n  Pick 1-2", "1")

    if choice == "2":
        edit_campaign()
        return

    print()
    print("  This walks you through creating a campaign step by step.")
    print("  Your campaign will be saved as a ready-to-play folder.")
    print(f"  See {REF_FILE} for all valid values.")
    print("  See README.md (included in the release) if you get stuck.\n")

    campaign_name = ask("Campaign name", "My Campaign")

    # Act Sequence
    print("\n  ---- ACT SEQUENCE ----")
    print("  Your campaign is built from maps. Each map is one number.")
    print("  You list the maps in order, separated by commas.\n")
    print("  MAP TYPES:")
    print("    1 = Act 1 — longest map. 4 games normally, 5 if Spartans replaced.")
    print("        Has a Spartan 3v3 challenge (can be replaced with a 5v5 game).")
    print("    2 = Act 2 — 3 games per map.")
    print("    3 = Act 3 — 3 games, FINAL BOSS. MUST be the last number.")
    print("        Can only be used ONCE. Campaign ends when you beat the Act 3 boss.\n")
    print("  EXAMPLES:")
    print("    1, 2, 3           = 3 maps, ~10 games (short campaign)")
    print("    1, 1, 2, 2, 3     = 5 maps, ~17 games (medium campaign)")
    print("    1, 2, 1, 2, 2, 3  = 6 maps, ~20 games (long campaign)")
    print("    2, 2, 2, 2, 3     = 5 maps, 15 games (no Act 1s)\n")

    while True:
        act_seq = ask("Act Sequence", "1, 2, 3")
        try:
            acts = [int(a.strip()) for a in act_seq.split(",")]
        except:
            print("  ERROR: Use numbers separated by commas (e.g. 1, 2, 3)")
            continue
        # Validate
        if not acts:
            print("  ERROR: Enter at least one number.")
            continue
        if acts[-1] != 3:
            print("  ERROR: Must end with 3 (the final boss act).")
            continue
        if acts.count(3) > 1:
            print("  ERROR: Act 3 can only appear once (it ends the campaign).")
            continue
        if any(a not in (1, 2, 3) for a in acts):
            print("  ERROR: Only use 1, 2, or 3.")
            continue
        break

    # Per-act Spartan replacement
    act1_indices = [i for i, a in enumerate(acts) if a == 1]
    replace_acts = []
    if act1_indices:
        if len(act1_indices) == 1:
            rep = ask_yn("Replace Spartan 3v3 with 5v5 elite on the Act 1 map?", "yes")
            if rep:
                replace_acts = [act1_indices[0]]
        else:
            print(f"\n  You have {len(act1_indices)} Act 1 maps. Each has a Spartan 3v3 challenge.")
            print("  Choose which ones to replace with a 5v5 elite game:\n")
            for idx, ai in enumerate(act1_indices):
                map_num = ai + 1
                rep = ask_yn(f"  Map {map_num} (Act 1 #{idx+1}) — replace Spartans with 5v5?", "yes")
                if rep:
                    replace_acts.append(ai)

    replace_all_challenges = len(replace_acts) == len(act1_indices) and len(act1_indices) > 0
    replace_no_challenges = len(replace_acts) == 0

    # Reminder about what Spartan replacement means
    if replace_all_challenges:
        print("\n  All Spartan 3v3 challenges will be replaced with 5v5 elite games.")
        print("  This adds 1 extra game per Act 1 map (you need more teams).")
    elif not replace_no_challenges:
        replaced = [ai + 1 for ai in replace_acts]
        print(f"\n  Spartans replaced on map(s): {replaced}. Others keep 3v3 challenges.")
    elif act1_indices:
        print("\n  Spartan 3v3 challenges kept on all Act 1 maps (no extra games).")

    print("\n  ---- MINIGAME SETTINGS ----")
    print("  The game has soccer and golf minigames with special balls.")
    print("  You can replace those balls with a regular puck.\n")
    replace_soccer = ask_yn("Replace soccer ball with puck?", "yes")
    replace_golf = ask_yn("Replace golf ball with puck?", "yes")

    # Count games
    total_games = 0
    for i, a in enumerate(acts):
        if a == 1:
            total_games += 5 if i in replace_acts else 4
        else:
            total_games += 3

    print(f"\n  Your campaign: {len(acts)} maps, {total_games} games, {total_games} teams needed.\n")

    # Build Replace Challenges value
    if replace_all_challenges:
        replace_str = "yes"
    elif replace_no_challenges:
        replace_str = "no"
    else:
        # Per-act: list which act numbers get replaced
        replace_act_nums = [str(acts[i]) for i in replace_acts]
        replace_str = ", ".join(replace_act_nums)

    # Build header
    output = []
    output.append(f"========================================\n")
    output.append(f"     {campaign_name.upper()}\n")
    output.append(f"========================================\n\n")
    output.append(f"--- Campaign Settings ---\n")
    output.append(f"Act Sequence            = {act_seq}\n")
    output.append(f"Replace Challenges      = {replace_str}\n")
    output.append(f"Replace Soccer Ball     = {'yes' if replace_soccer else 'no'}\n")
    output.append(f"Replace Golf Ball       = {'yes' if replace_golf else 'no'}\n")
    output.append(f"\n\n")

    # Figure out bosses and which act each game belongs to
    game_num = 0
    boss_games = set()
    game_to_act = {}
    for i, a in enumerate(acts):
        games_in_map = (5 if i in replace_acts else 4) if a == 1 else 3
        for g in range(games_in_map):
            game_num += 1
            game_to_act[game_num] = a
        boss_games.add(game_num)

    # Create teams
    for i in range(1, total_games + 1):
        is_final = (i == total_games)
        is_boss = i in boss_games
        act_num = game_to_act.get(i, 0)
        team_lines = create_team(i, is_boss=is_boss, is_final=is_final, act_num=act_num)
        output.extend(team_lines)

    # Save
    folder = os.path.join(CAMPAIGNS_DIR, campaign_name)
    os.makedirs(folder, exist_ok=True)
    filepath = os.path.join(folder, "campaign.txt")
    with open(filepath, "w") as f:
        f.writelines(output)

    print(f"\n{'='*50}")
    print(f"  SAVED!")
    print(f"  Folder: {folder}")
    print(f"  File:   campaign.txt")
    print(f"{'='*50}")
    print(f"\n  To play, edit active.txt:")
    print(f"    Active Campaign          = {campaign_name}")
    print(f"\n  Done!")


if __name__ == "__main__":
    main()
