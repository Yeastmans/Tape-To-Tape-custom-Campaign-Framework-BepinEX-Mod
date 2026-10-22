using EndlessMode;
using Data;
using Tape2Tape.Hockey.UI;
using UnityEngine;
int count=0; void Check(bool b,string name){if(!b)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
var g=new GoaltenderData{skill=-7,catchingSkill=11,gloveSkill=12,blockerSkill=13,fiveHoleSkill=14,standingSpeed=150,butterflySpeed=170,controlSkill=17,recoverySkill=18,passPower=19,pokecheckSkill=20,depth=21,passReadSkill=.125f};
var values=GoalieGmPreview.Stats(g);
Check(values.Length==13,"AllThirteenAuthoredGoalieStats");
Check(values.Any(x=>x.Name=="Skill"&&x.Value=="-7")&&values.Any(x=>x.Name=="Standing Speed"&&x.Value=="150")&&values.Any(x=>x.Name=="Pass Power"&&x.Value=="19")&&values.Any(x=>x.Name=="Depth"&&x.Value=="21"),"NamedFieldsNotIncorrectNativeEnumFallback");
Check(values.Any(x=>x.Name=="Pass Read"&&x.Value=="0.125")&&!values.Any(x=>x.Name.Contains("Shot")),"PassReadAuthoredUnitsAndShotPowerExcluded");
var menu=new EventChoiceMenu();var item=new EventChoiceItem();item.ChoiceData.Skater=g;menu.m_Items.Add(item);
var parent=new RectTransform();foreach(var row in new[]{item.m_SkaterDetails.m_ForwardSpeed,item.m_SkaterDetails.m_ForwardAccuracy,item.m_SkaterDetails.m_ForwardShotPower,item.m_SkaterDetails.m_ForwardChecking}) {row.transform.parent=parent;row.gameObject.SetActive(false);}
item.m_SkaterDetails.m_ForwardSpeed.m_NameLabel=null;
item.m_SkaterDetails.m_ForwardSpeed.transform.Children["LabelBar"]=new Transform {Component=new TMPro.TMP_Text()};
((RectTransform)item.m_SkaterDetails.m_ForwardSpeed.transform).anchoredPosition=new(63,-211);
((RectTransform)item.m_SkaterDetails.m_ForwardSpeed.transform).sizeDelta=new(311,28);
var offer=new GoalieRecruitment.Offer{Candidate=g};var visit=new GoalieRecruitment.Visit();visit.Selection.Menu=menu;visit.Offers.Add(offer);GoalieRecruitment.Current=visit;
menu.IsVisible=false;GoalieGmPreview.Tick();Check(UnityEngine.Object.Created==0,"HiddenMenuCreatesNoPresentation");
menu.IsVisible=true;GoalieGmPreview.Tick();Check(UnityEngine.Object.Created==26,"VisibleExactOfferCreatesOnlyThirteenLabelValuePairs");
GoalieGmPreview.Tick();Check(UnityEngine.Object.Created==26,"SteadyTickDoesNotAllocateNativeObjects");
foreach(var text in UnityEngine.Object.Texts) {
 var r=text.rectTransform;
 Check(r.anchoredPosition.x>=63 && r.anchoredPosition.x+r.sizeDelta.x<=374
    && r.anchoredPosition.y<=-211 && r.anchoredPosition.y-r.sizeDelta.y>=-351,
    "OwnedTextStaysInsideReviewedStatsRegion:"+text.text);
}
Check(UnityEngine.Object.Texts.Single(t=>t.text=="0.125").rectTransform.sizeDelta.x>=100,"FractionalPassReadHasRoomForFullAuthoredPrecision");
Check(UnityEngine.Object.Texts.Single(t=>t.text=="-7").rectTransform.sizeDelta.x>=48,"SignedSkillHasRoomForNativeMinimum");
Check(UnityEngine.Object.Texts.All(t=>!t.raycastTarget&&!t.enableWordWrapping&&t.maxVisibleLines==1),"StatsCannotStealInputOrWrapAcrossRows");
Check(UnityEngine.Object.Texts.Where((t,i)=>i%2==1).Select(t=>t.text).SequenceEqual(values.Select(v=>v.Value)),"AllDisplayedValuesMatchNamedFields");
item.m_SkaterDetails.m_ForwardSpeed.gameObject.SetActive(true);GoalieGmPreview.Tick();Check(!item.m_SkaterDetails.m_ForwardSpeed.gameObject.activeSelf,"NativeSelectionRefreshCannotShowForwardRows");
item.ChoiceData.Skater=new();item.m_SkaterDetails.m_ForwardSpeed.gameObject.SetActive(true);GoalieGmPreview.Tick();Check(UnityEngine.Object.Removed==26&&item.m_SkaterDetails.m_ForwardSpeed.gameObject.activeSelf,"ForeignCardReuseRemovesOnlyOwnedClones");
Check(UnityEngine.Object.Texts.All(t=>!t.gameObject.activeSelf),"OwnedObjectsHiddenBeforeDeferredDestruction");
item.ChoiceData.Skater=g;GoalieGmPreview.Tick();Check(UnityEngine.Object.Created==52,"ReturnedExactOfferRebuildsOnce");
GoalieRecruitment.Current=null;GoalieGmPreview.Tick();Check(UnityEngine.Object.Removed==52&&item.m_SkaterDetails.m_ForwardSpeed.gameObject.activeSelf,"ClosedVisitRestoresOwnPriorVisibility");
GoalieRecruitment.Current=visit;offer.Taken=true;GoalieGmPreview.Tick();Check(UnityEngine.Object.Created==52,"ConsumedOfferHasNoPreview");
offer.Taken=false;GoalieRecruitment.ContextValid=false;GoalieGmPreview.Tick();Check(UnityEngine.Object.Created==52,"StaleCampaignVisitHasNoPreview");
GoalieRecruitment.ContextValid=true;menu.IsHiding=true;GoalieGmPreview.Tick();Check(UnityEngine.Object.Created==52,"ClosingMenuHasNoPreview");
menu.IsHiding=false;((RectTransform)item.m_SkaterDetails.m_ForwardSpeed.transform).sizeDelta=new(400,28);
GoalieGmPreview.Tick();GoalieGmPreview.Tick();Check(UnityEngine.Object.Created==52&&Plugin.Log.Warnings==1,"UnknownLayoutWarnsOnceWithoutAllocatingOrLooping");
GoalieRecruitment.Current=null;GoalieGmPreview.Tick();
if(args.Length==2) InteropChecks.Run(args,Check);
Console.WriteLine($"PASS {count} source-linked goalie GM presentation checks");
