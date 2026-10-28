using Data;
using EndlessMode;
using Rogue.NPC.GeneralManagerNPC;
using Tape2Tape;
using Tape2Tape.Hockey.UI;
using Tape2Tape.Customization.UI;
using IlList = Il2CppSystem.Collections.Generic.List<Data.ForwardData>;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
(TeamData team, GeneralManagerEvent node, EventChoiceMenu gm) Setup(int hires = 2, bool empty = false, bool contracts = true)
{
 SquadBehaviorRuntime.Two = true; SquadBehaviorRuntime.Contracts = contracts; SquadBehaviorRuntime.Default = false;
 CustomFreeAgentTemplates.Active = false; CampaignUtil.CampaignState.freeAgents.Clear(); CampaignUtil.CampaignState._unlockSystem.Locked.Clear();
 TwoLineContracts.Current = null; TwoLineContracts.Generating = null; TwoLineContracts.Inserting = null; TwoLineContractRoster.Current = null; T2TUIModule.FailOpening = false;
 var team = new TeamData(); team.lines.Add(new()); team.lines.Add(new()); var run = new RunData { teamData = team }; CampaignUtil.CampaignState.runData = run;
 for (int i=0;i<10;i++) { var p = empty ? null : new ForwardData { id=$"p{i}", FullName=$"Player {i}", Jersey=i+1 }; team.forwards.Add(p); TwoLineContracts.SetSlot(team,i,p?.id ?? ""); if(contracts && p!=null && i!=2) run.freeAgents.Add(new(){id=p.id}); }
 var node = new GeneralManagerEvent { NumTimesToOfferChoices=hires }; node.NextChoices(0); var gm=new EventChoiceMenu { SourceNode=node }; gm.LoadOffers(); return (team,node,gm);
}
void Consistent(TeamData team)
{
 Check(team.forwards.Count>=10,"Ten stable positions retained");
 for(int i=0;i<10;i++) Check(TwoLineContracts.SlotId(team,i)==(team.forwards[i]?.id ?? ""),$"Slot {i} maps by ID");
 Check(team.forwards.Where(p=>p!=null).Select(p=>p.id).Distinct().Count()==team.forwards.Count(p=>p!=null),"No duplicate IDs");
}
LineupMenu Recruit(EventChoiceMenu gm,int index=0)
{
 Check(!TwoLineContractRecruitClick.Prefix(gm,gm.m_Items[index].Navigable),"Recruit click intercepts native GM choose");
 var menu=TwoLineContractRoster.Current?.Menu;
 Check(menu!=null && !gm.Visible && gm.m_IsTakenOver,"GM hides while roster owns input");
 Check(menu.m_ActivePlayersList.m_SkaterCards.Count(c=>!c.Locked)==5 && menu.m_BenchPlayersList.m_SkaterCards.Count(c=>!c.Locked)==5,"Five selectable positions on each line");
 Check(menu.m_ActivePlayersList.m_SkaterCards.Single(c=>c.CurrentSkaterData.ActivePosition==ESkaterPosition.Goalie).Locked,"Goalie locked");
 Check(menu.FirstSelections==1,"Controller focus enters empty or full roster"); return menu;
}
SkaterCard Card(LineupMenu menu,int slot)=>menu.m_ActivePlayersList.m_SkaterCards.Concat(menu.m_BenchPlayersList.m_SkaterCards).Single(c=>TwoLineContractRoster.Slot(c.CurrentSkaterData)==slot);
void Select(LineupMenu menu,int slot) { var card=Card(menu,slot); card.Toggle.IsSelected=true; Check(!TwoLineContractRosterPick.Prefix(menu,card),"Position selection bypasses lineup swaps"); }

// Every occupied and empty destination, including Line 2 with live Line 2 active.
foreach(bool contracts in new[]{false,true}) foreach(bool empty in new[]{false,true}) for(int slot=0;slot<10;slot++)
{
 var(team,node,gm)=Setup(1,empty,contracts); team.activeLineNumber=2; var before=team.forwards.ToArray(); if(!empty) CampaignUtil.CampaignState.runData.forwardInjuries[before[slot].id]="arm";
 var recruit=gm.m_Items[0].ChoiceData.Skater; var menu=Recruit(gm); var selection=TwoLineContractRoster.Current;
 Check(node.NativeCallbacks==0 && team.forwards.SequenceEqual(before),"Opening never hires or removes players");
 Check(team.activeLineNumber==2 && selection.Preview.GetActiveLine()==1,"Preview uses canonical Line 1/2 without changing live line");
 TwoLineContractRosterPick.Prefix(menu,Card(menu,slot)); Check(node.NativeCallbacks==0,"Toggle-off cannot hire");
 var goalie=menu.m_ActivePlayersList.m_SkaterCards.Last(); goalie.Toggle.IsSelected=true; TwoLineContractRosterPick.Prefix(menu,goalie); Check(node.NativeCallbacks==0,"Goalie callback cannot replace goalie");
 Select(menu,slot); Check(team.forwards[slot]==recruit && node.NativeCallbacks==1 && TwoLineContracts.Current.Hires==1,"One native hire at selected position");
 TwoLineContracts.Commit(selection.Visit,selection.Offer,slot); TwoLineContractRoster.Finish(selection); Check(node.NativeCallbacks==1 && gm.Exits==1,"Duplicate callbacks cannot hire or exit twice");
 Check(!gm.m_IsTakenOver && gm.m_MenuOverSelf==null && TwoLineContractRoster.Current==null,"Final signing releases both menus");
 Check(team.activeLineNumber==2,"Placement preserves active line"); for(int i=0;i<10;i++) if(i!=slot) Check(team.forwards[i]==before[i],"Unselected positions unchanged");
 var run=CampaignUtil.CampaignState.runData; if(!empty){Check(!run.freeAgents.Any(x=>x.id==before[slot].id),"Outgoing contract removed");Check(!run.forwardInjuries.ContainsKey(before[slot].id),"Outgoing injury removed");}
 Check(run.freeAgents.Count(x=>x.id==recruit.id && x.Matches==6)==(contracts ? 1 : 0),"Native contract policy retained without adding expiry to permanent recruits");
 Check(((ForwardData)recruit).Stat==40 && ((ForwardData)recruit).Talent=="Original talent","Native stats and talents retained");
 Check(team.forwards.Where(p=>p!=null).Select(p=>p.Jersey).Distinct().Count()==team.forwards.Count(p=>p!=null),"Unique jersey allocation"); Consistent(team);
}
// Fill an entirely empty squad in arbitrary order, leaving the selector between hires.
{
 var(team,node,gm)=Setup(10,true); team.forwards=null; TwoLineContractExpiry.Postfix(); Check(team.forwards.Count==10 && team.forwards.All(p=>p==null),"Empty storage reconciles to ten vacancies");
 foreach(int slot in new[]{9,0,8,1,7,2,6,3,5,4}) Select(Recruit(gm),slot);
 Check(team.forwards.All(p=>p!=null) && node.NativeCallbacks==10 && gm.Refreshes==9 && gm.Exits==1,"Ten explicit hires fill empty squad and respect limit"); Consistent(team);
}
// Back preserves offers and signings with full or empty roster; stale callbacks cannot act.
foreach(bool contracts in new[]{false,true}) foreach(bool empty in new[]{false,true})
{
 var(team,node,gm)=Setup(2,empty,contracts); var offers=node.eventChoices.Select(c=>c.Skater.id).ToArray(); var menu=Recruit(gm); var stale=TwoLineContractRoster.Current; menu.TryLeaveMenu(false);
 Check(gm.Visible && !gm.m_IsTakenOver && gm.Exits==0 && gm.Refreshes==0,"Back restores GM directly"); Check(node.Generations==1 && node.NativeCallbacks==0 && TwoLineContracts.Current.Hires==0,"Cancel never rerolls or spends signing");
 Check(node.eventChoices.Select(c=>c.Skater.id).SequenceEqual(offers),"Same offers retained"); var second=Recruit(gm,1); TwoLineContracts.Commit(stale.Visit,stale.Offer,0); Check(node.NativeCallbacks==0,"Cancelled offer cannot commit later");
 Select(second,7); Check(gm.Visible && gm.Refreshes==1 && gm.Exits==0,"Remaining signing returns to new offers"); Consistent(team);
}
// Compacted expiry preserves survivor positions and both vacancies can be selected.
{
 var(team,node,gm)=Setup(); team.activeLineNumber=2; var survivor=team.forwards[7]; team.forwards=new IlList{null,team.forwards[1],team.forwards[2],team.forwards[3],team.forwards[4],team.forwards[5],survivor,team.forwards[8],team.forwards[9]};
 TwoLineContractExpiry.Postfix(); Check(team.forwards[0]==null && team.forwards[6]==null && team.forwards[7]==survivor,"Expiry preserves original positions"); Select(Recruit(gm),6); Select(Recruit(gm),0); Consistent(team);
}
// Rollback and retry without closing selector or losing original contract.
foreach(bool skip in new[]{false,true})
{
 var(team,node,gm)=Setup(1); var old=team.forwards[6]; node.ThrowAfterInsertion=!skip; node.SkipInsertion=skip; var menu=Recruit(gm); string id=TwoLineContractRoster.Current.Offer.Player.id; Select(menu,6);
 Check(team.forwards[6]==old && TwoLineContracts.Current.Hires==0 && !menu.IsHiding,"Failed commit preserves player and leaves selector open");
 Check(!CampaignUtil.CampaignState.runData.freeAgents.Any(a=>a.id==id) && CampaignUtil.CampaignState.runData.freeAgents.Any(a=>a.id==old.id),"Rollback preserves old contract and removes phantom new one");
 Check(TwoLineContracts.Inserting==null && !Card(menu,6).Toggle.IsSelected,"Error restores scope and allows another click"); node.ThrowAfterInsertion=node.SkipInsertion=false; Select(menu,6); Check(gm.Exits==1,"Retry succeeds and closes"); Consistent(team);
}
// Loading error restores GM without signing or rerolling.
{
 var(_,node,gm)=Setup(); T2TUIModule.FailOpening=true; TwoLineContractRecruitClick.Prefix(gm,gm.m_Items[0].Navigable);
 Check(gm.Visible && !gm.m_IsTakenOver && TwoLineContractRoster.Current==null && TwoLineContracts.Current.Pending==null,"Failed opening restores GM input"); Check(node.NativeCallbacks==0 && node.Generations==1,"Opening failure does not sign or reroll");
}
// Both legacy and modern paths use the same ten-slot selector.
{
 var(team,node,_)=Setup(1); var panel=new EventChoicePanel(); TwoLineContractLegacyPanel.Postfix(panel,node); node.eventChoices[0].onSelection.Invoke(); Check(!TwoLineContractLegacyAdvance.Prefix(panel),"Legacy GM opens selector");
 Check(panel._canvasGroup.alpha==0 && !panel._canvasGroup.interactable,"Legacy GM hides"); Check(!TwoLineContractLegacyAdvance.Prefix(panel),"Duplicate legacy hide suppressed");
 TwoLineContractRoster.Current.Menu.TryLeaveMenu(false); Check(panel.Hides==0 && panel._canvasGroup.alpha==1,"Legacy cancel restores same panel");
 node.eventChoices[0].onSelection.Invoke(); TwoLineContractLegacyAdvance.Prefix(panel); Select(TwoLineContractRoster.Current.Menu,9); Check(panel.Hides==1 && node.NativeCallbacks==1,"Legacy final signing closes once"); Consistent(team);
}
// Orphan recovery, no-expiry, scopes and unrelated modes.
{
 var(team,node,gm)=Setup(); var hired=new ForwardData{id="orphan",Stat=99}; team.forwards[6]=hired; var reserve=new ForwardData{id="extra"}; team.forwards.Add(reserve); TwoLineContractLoadedRoster.Postfix(CampaignUtil.CampaignState.runData);
 Check(team.forwards[6]==hired && !team.forwards.Any(p=>p?.id=="p6") && team.forwards[10]==reserve,"Saved orphan and reserve preserved, no resurrection"); var before=team.forwards.ToArray(); TwoLineContractExpiry.Postfix(); Check(team.forwards.SequenceEqual(before),"No-expiry repair idempotent"); Consistent(team);
 foreach(var mode in new[]{"single","default"}){SquadBehaviorRuntime.Contracts=true;SquadBehaviorRuntime.Two=mode!="single";SquadBehaviorRuntime.Default=mode=="default";Check(!TwoLineContracts.Applies(team) && TwoLineContractRecruitClick.Prefix(gm,gm.m_Items[0].Navigable),"Unrelated mode excluded: "+mode);}
 SquadBehaviorRuntime.Contracts=SquadBehaviorRuntime.Two=true; SquadBehaviorRuntime.Default=false; node.ThrowDuringGeneration=true; try{node.NextChoices(0);}catch(InvalidOperationException){}
 Check(TwoLineContracts.Generating==null,"Generation failure restores scope"); int count=5; Check(TwoLineContractOfferRoom.Prefix(team,ref count) && count==5,"Normal count untouched");
}
Check(Il2CppInterop.Runtime.IL2CPP.UnboxedAdds>0,"Unboxed native card insertion exercised");
// Per-player contracts do not require the GM squad behavior.
{
 var(team,node,gm)=Setup(1);SquadBehaviorRuntime.Contracts=false;
 Check(TwoLineContracts.Applies(team),"Non-GM two-line contracts use explicit placement");
 CampaignUtil.CampaignState.runData.freeAgents.Clear();
 Check(TwoLineContracts.Applies(team),"Full two-line roster without contracts still supports explicit GM placement");
 team.forwards.RemoveAt(8);TwoLineContractExpiry.Postfix();
 Check(team.forwards.Count==10&&team.forwards[8]==null&&TwoLineContracts.SlotId(team,8)=="", "Final non-GM expiry leaves its actual second-line vacancy");
 Check(TwoLineContracts.Applies(team),"Vacancy remains recruitable after final contract expires");
 node.NextChoices(0);gm.LoadOffers();Select(Recruit(gm),8);
 Check(team.forwards[8]?.id!=null&&node.NativeCallbacks==1,"Non-GM hire fills explicit second-line vacancy");
}
// Closing is asynchronous in Unity: suppress repeat input until the hide event.
{
 var(team,node,gm)=Setup(2); var menu=Recruit(gm); var selection=TwoLineContractRoster.Current; menu.DeferClose=true;
 Select(menu,8); Select(menu,0); Check(node.NativeCallbacks==1 && gm.Refreshes==0 && !gm.Visible,"No duplicate hire or early GM advance during roster outro");
 menu.OnHidingCompleted.Invoke(menu); menu.OnDestroy.Invoke(menu); Check(gm.Refreshes==1 && gm.Visible,"Hide and destroy notifications advance only once");
 Check(menu.OnHidingCompleted.Actions.Count==0 && menu.OnDestroy.Actions.Count==0,"Selector unregisters close callbacks for retained menu reuse"); Consistent(team);
}
// A changed target or an old visit must never replace an unseen player.
{
 var(team,node,gm)=Setup(); var menu=Recruit(gm); TwoLineContracts.SetSlot(team,4,"changed-target"); Select(menu,4);
 Check(node.NativeCallbacks==0,"Stale target rejected"); TwoLineContracts.SetSlot(team,4,team.forwards[4].id);
 TwoLineContractVisitStart.Prefix(); Select(menu,0); Check(node.NativeCallbacks==0,"Old visit selector cannot commit after visit reset");
 menu.TryLeaveMenu(false); Check(TwoLineContractRoster.Current==null,"Old selector still cleans up on close");
}
// The native GM indexes two offers even when its filtered custom pool has one.
// Exercise the production prefix/offer wrapping with one and zero candidates.
foreach (bool contracts in new[] { false, true })
{
 var(team,_,_)=Setup(2,contracts:contracts); TwoLineContracts.Current=null;
 CustomFreeAgentTemplates.Active=true;
 var last=new ForwardData{id="last-candidate",FullName="Brent Burns",Jersey=88};
 CampaignUtil.CampaignState.freeAgents.Add(last);
 var node=new GeneralManagerEvent{NumTimesToOfferChoices=2};
 Check(node.NextChoices(0) && node.eventChoices.Count==1,"One remaining custom candidate produces exactly one offer");
 Check(node.eventChoices[0].Skater.FullName=="Brent Burns" && node._previouslyOfferedNames.Contains("Brent Burns"),"Single offer retains its authored name and native offered-name tracking");
 var gm=new EventChoiceMenu{SourceNode=node};gm.LoadOffers();
 Recruit(gm).TryLeaveMenu(false);
 Check(gm.Visible && node.eventChoices.Count==1 && node.NativeCallbacks==0,"Cancel retains the one remaining offer without consuming it");
 Select(Recruit(gm),0);
 Check(team.forwards[0].FullName=="Brent Burns" && node.NativeCallbacks==1 && gm.Exits==1,"Single-candidate hire completes and exhausted node closes safely");
 Check(CampaignUtil.CampaignState.runData.freeAgents.Count(a=>a.id==last.id)==(contracts?1:0),"Small pools preserve contract-on and contract-off signing policy");
 Consistent(team);
}
{
 var(team,_,_)=Setup(contracts:false); TwoLineContracts.Current=null;CustomFreeAgentTemplates.Active=true;
 var node=new GeneralManagerEvent{NumTimesToOfferChoices=2};node.eventChoices.Add(new(){Skater=new ForwardData{id="stale"}});
 Check(!node.NextChoices(0) && node.eventChoices.Count==0,"Explicit empty custom pool clears stale offers and ends safely");
 var ownedId=new ForwardData{id=team.forwards[0].id,FullName="Other"};
 var ownedName=new ForwardData{id="other-id",FullName=team.forwards[1].FullName};
 var previous=new ForwardData{id="previous",FullName="Previously Offered"};
 var locked=new ForwardData{id="locked",FullName="Locked"};
 var excluded=new ForwardData{id="excluded",FullName="Excluded",Excluded=true};
 foreach(var player in new[]{ownedId,ownedName,previous,locked,excluded})CampaignUtil.CampaignState.freeAgents.Add(player);
 node._previouslyOfferedNames.Add(previous.FullName);CampaignUtil.CampaignState._unlockSystem.Locked.Add(locked.id);
 Check(!node.NextChoices(0) && node.eventChoices.Count==0,"Eligibility keeps owned IDs/names, previous offers, locks and squad exclusions out");
 CampaignUtil.CampaignState.freeAgents.Add(new(){id="gawain",firstName="Gawain",FullName="Gawain Native"});CampaignUtil.CampaignState.runData.CurrentAct=2;
 Check(!node.NextChoices(0) && node.eventChoices.Count==0,"Native Gawain act restriction remains enforced in small pools");
 CampaignUtil.CampaignState.runData.CurrentAct=1;
 Check(node.NextChoices(0) && node.eventChoices.Count==1,"Native Gawain candidate remains eligible in Act 1");
 CampaignUtil.CampaignState.freeAgents.Add(new(){id="one",FullName="One"});CampaignUtil.CampaignState.freeAgents.Add(new(){id="two",FullName="Two"});
 bool result=false;int count=0;
 Check(TwoLineContractChoices.Prefix(node,ref count,ref result,out var scope),"Two eligible candidates keep the native random selection path");TwoLineContractChoices.Finalizer(scope);
}
Console.WriteLine($"PASS: {checks} source-linked roster/recruitment checks. Native UI/gameplay validation remains pending.");
