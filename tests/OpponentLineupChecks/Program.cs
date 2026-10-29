using Data;
using EndlessMode;
using STS.Map;
using Tape2Tape.Hockey.UI;
using System.Reflection;

int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) { Console.Error.WriteLine("FAIL: " + name); Environment.Exit(1); } }
TeamData Team()
{
    var team = new TeamData();
    for (int i = 0; i < 10; i++) team.forwards.Add(new() { id = "player" + i, FullName = "CHI Player " + i, speed = 30 + i });
    for (int i = 0; i < 10; i += 5) team.lines.Add(new() { leftWinger = "player" + i, rightWinger = "player" + (i+1), center = "player" + (i+2), leftDefensemen = "player" + (i+3), rightDefensemen = "player" + (i+4) });
    return team;
}
// Native ReplaceForwardWithSubstitute replaces the list entry only. Invoke the
// production patch if present; absence reproduces the released game's behavior.
void Substitute(MatchMapNode node, ForwardData old, ForwardData replacement, bool change = true)
{
    var patch = typeof(LineupPreviewRoster).Assembly.GetType("EndlessMode.OpponentLineupSubstitutes");
    var args = new object[] { node, old, null };
    patch?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    if (change && node.opponent.forwards.IndexOf(old) is int index && index >= 0) node.opponent.forwards[index] = replacement;
    patch?.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { (object)node, args[2] });
}
foreach (bool nativeCopy in new[] { false, true }) foreach (int active in new[] { 1, 2 })
{
    var team = Team(); if (nativeCopy) { team.id = "native-copy"; team.Owned = true; }
    team.activeLineNumber = active;
    var node = new MatchMapNode { opponent = team };
    for (int slot = 0; slot < 10; slot++)
    {
        var replacement = new ForwardData { id = "sub" + slot, speed = 90 };
        Substitute(node, team.forwards[slot], replacement);
        Check(team.lines.SelectMany(l => l.Ids).All(id => team.forwards.Any(p => p.id == id)), "Every lineup ID resolves after native substitution, slot " + slot);
        Check(team.forwards[slot] == replacement && replacement.speed == 90, "Native replacement and stats retained");
        Check(team.activeLineNumber == active, "Active line retained");
    }
    var menu = new LineupMenu { Team = new() { OriginalTeamData = team }, m_AreSkatersLocked = true };
    LineupInitialRoster.Postfix(menu);
    Check(menu.Team.m_BenchForwards.Count == 5 && menu.Team.m_BenchForwards.All(p => p != null), "Locked opponent preview contains five inactive skaters");
    menu.m_AreSkatersLocked = false; var pending = new ForwardData { id = "pending" }; menu.Team.m_BenchForwards[0] = pending;
    LineupPreviewRoster.Prefix(menu); Check(menu.Team.m_BenchForwards[0] == pending, "Editable pending swaps retained");
}
foreach (string mode in new[] { "default", "native", "single", "no-replacement", "unknown-player", "null-replacement" })
{
    var team = Team(); var node = new MatchMapNode { opponent = team };
    Plugin.IsDefaultMode = mode == "default";
    if (mode == "native") team.id = "native";
    if (mode == "single") team.lines.RemoveAt(1);
    var before = string.Join(",", team.lines.SelectMany(l => l.Ids));
    Substitute(node, mode == "unknown-player" ? new() { id = "absent" } : team.forwards[0], mode == "null-replacement" ? null : new() { id = "sub" }, mode != "no-replacement");
    Check(before == string.Join(",", team.lines.SelectMany(l => l.Ids)), "Unrelated or unsuccessful substitution untouched: " + mode);
    Plugin.IsDefaultMode = false;
}
// The actual prefix must prevent the native substitution stage from reading
// the recruitment pool or changing authored opponents, even with an empty pool.
bool Assign(MatchMapNode node, Action native)
{
    var patch = typeof(LineupPreviewRoster).Assembly.GetType("EndlessMode.PreserveAuthoredOpponentRoster");
    bool runNative = patch?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, [node]) is not false;
    if (runNative) native();
    return runNative;
}
foreach(bool copiedNative in new[]{false,true}) foreach(int lineCount in new[]{1,2}) foreach(bool emptyPool in new[]{false,true})
{
    var home=Team(); var opponent=Team();
    if(copiedNative){opponent.id="native-copy";opponent.Owned=true;}
    if(lineCount==1){opponent.lines.RemoveAt(1);opponent.forwards.RemoveRange(5,5);}
    opponent.activeLineNumber=lineCount;
    var before=opponent.forwards.ToArray();var homeBefore=home.forwards.ToArray();var goalie=opponent.goalie;
    var ids=string.Join(",",opponent.lines.SelectMany(l=>l.Ids));
    int poolReads=0;
    Assign(new(){opponent=opponent},()=>{
        poolReads++;
        if(emptyPool)return;
        for(int i=0;i<opponent.forwards.Count;i++)
            if(home.forwards.Any(p=>p.FullName==opponent.forwards[i].FullName))
                opponent.forwards[i]=new(){id="free-agent-"+i,FullName="Pool Extra "+i,speed=99};
    });
    Check(opponent.forwards.SequenceEqual(before),"Mirror launch keeps every authored opponent player");
    Check(poolReads==0,"Mirror launch never reads the squad recruitment pool");
    Check(home.forwards.SequenceEqual(homeBefore),"Mirror launch leaves the player roster untouched");
    Check(opponent.goalie==goalie && opponent.activeLineNumber==lineCount,"Goalie and active line stay unchanged");
    Check(string.Join(",",opponent.lines.SelectMany(l=>l.Ids))==ids,"Both line assignments retain their original player IDs");
    for(int i=0;i<opponent.forwards.Count;i++){
        var player=opponent.forwards[i];
        Check(player.FullName=="CHI Player "+i && player.id=="player"+i && player.speed==30+i && player.equipment=="Authored kit" && player.talent=="Authored talent","Mirror player name, ID, stats, kit and talent retained");
    }
}
foreach(string mode in new[]{"default-custom","ordinary-native","missing-opponent","missing-node"})
{
    var opponent=Team();var node=new MatchMapNode{opponent=opponent};Plugin.IsDefaultMode=mode=="default-custom";
    if(mode=="ordinary-native")opponent.id="base-game";
    if(mode=="missing-opponent")node.opponent=null;
    if(mode=="missing-node")node=null;
    int calls=0;Check(Assign(node,()=>calls++) && calls==1,"Native substitution remains authoritative: "+mode);
    Plugin.IsDefaultMode=false;
}
Console.WriteLine($"PASS: {checks} opponent lineup and mirror-roster checks; native match verification is separate.");
