using System;
using System.Collections.Generic;
using System.Globalization;
using Data;
using Tape2Tape.Customization.UI;
using Tape2Tape.Hockey.UI;
using TMPro;
using UnityEngine;

namespace EndlessMode;

// Presentation only. Native condition rows own the contract preview; these
// detached text widgets fill the GM prefab's missing detailed goalie fields.
internal static class GoalieGmPreview
{
    private sealed class Card
    {
        internal EventChoiceItem Item;
        internal GoalieRecruitment.Offer Offer;
        internal IntPtr Candidate, Details;
        internal GameObject[] Originals;
        internal bool[] Visible;
        internal readonly List<TMP_Text> Text = new();
        internal long Seen;
    }
    private static readonly Dictionary<IntPtr, Card> cards = new();
    private static readonly List<IntPtr> expired = new();
    private static long frame;
    internal static (string Name, string Value)[] Stats(GoaltenderData goalie)
    {
        string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        // Native GetBaseStat falls back to skill for several of these enums.
        // Named fields preserve authored units, especially the 0..1 Pass Read.
        return new[] {
            ("Skill", Number(goalie.skill)), ("Catching", Number(goalie.catchingSkill)),
            ("Glove", Number(goalie.gloveSkill)), ("Blocker", Number(goalie.blockerSkill)),
            ("Five Hole", Number(goalie.fiveHoleSkill)), ("Standing Speed", Number(goalie.standingSpeed)),
            ("Butterfly Speed", Number(goalie.butterflySpeed)), ("Control", Number(goalie.controlSkill)),
            ("Recovery", Number(goalie.recoverySkill)), ("Pass Power", Number(goalie.passPower)),
            ("Poke Check", Number(goalie.pokecheckSkill)), ("Depth", Number(goalie.depth)),
            ("Pass Read", goalie.passReadSkill.ToString("0.#########", CultureInfo.InvariantCulture))
        };
    }
    internal static void Tick()
    {
        frame++;
        var visit = GoalieRecruitment.Current;
        var menu = visit?.Selection?.Menu;
        if (GoalieRosterMenu.Enabled && GoalieRecruitment.IsCurrent(visit) && menu != null && menu.IsVisible && !menu.IsHiding)
        {
            for (int i = 0; i < menu.m_Items.Count; i++)
            {
                var item = menu.m_Items[i];
                if (item == null || item.m_SkaterDetails == null) continue;
                GoalieRecruitment.Offer offer = null;
                foreach (var candidate in visit.Offers)
                    if (GoalieRecruitment.Valid(candidate) && candidate.Candidate?.Pointer == item.ChoiceData.Skater?.Pointer) { offer = candidate; break; }
                if (offer == null) continue;
                if (cards.TryGetValue(item.Pointer, out var card) && (!ReferenceEquals(card.Offer, offer) || card.Details != item.m_SkaterDetails.Pointer))
                { Remove(card); cards.Remove(item.Pointer); card = null; }
                if (card == null)
                {
                    card = new Card { Item = item, Offer = offer, Candidate = offer.Candidate.Pointer, Details = item.m_SkaterDetails.Pointer };
                    cards.Add(item.Pointer, card);
                    try { Create(card); }
                    catch (Exception ex)
                    {
                        Remove(card);
                        // Keep the failed identity until the offer changes: no
                        // repeated allocation or per-frame warning on drift.
                        card.Originals = null;
                        Plugin.Log.LogWarning("[GoaliePreview] Native GM stat layout unavailable: " + ex.Message);
                    }
                }
                card.Seen = frame;
                if (card.Originals != null)
                {
                    foreach (var original in card.Originals) if (original != null && original.activeSelf) original.SetActive(false);
                    foreach (var text in card.Text) if (text != null && !text.gameObject.activeSelf) text.gameObject.SetActive(true);
                }
            }
        }
        expired.Clear();
        foreach (var pair in cards) if (pair.Value.Seen != frame) { Remove(pair.Value); expired.Add(pair.Key); }
        foreach (var key in expired) cards.Remove(key);
    }
    private static void Create(Card card)
    {
        var details = card.Item.m_SkaterDetails;
        SkaterStatBox[] rows = { details.m_ForwardSpeed, details.m_ForwardAccuracy, details.m_ForwardShotPower, details.m_ForwardChecking };
        if (Array.Exists(rows, row => row == null)) throw new InvalidOperationException("Expected four native GM stat rows.");
        var template = rows[0];
        var rect = template.transform.TryCast<RectTransform>();
        // All three shipped GM prefabs leave m_NameLabel null. Their visible
        // label is the exact LabelBar child (TMP + layout, no stat behavior).
        var nameLabel = template.m_NameLabel ?? template.transform.Find("LabelBar")?.GetComponent<TMP_Text>();
        if (rect == null || nameLabel == null || template.m_MainValue == null || rect.parent == null)
            throw new InvalidOperationException("Expected native GM stat text and rectangle.");
        // Match the original four stat rows, not the larger gap above talents.
        // Native Noto needs a 20px text box at 14px font size. Its actual glyphs
        // fit 15.5px row spacing; seven rows end at -324 inside y-211..-326.
        if (Math.Abs(rect.sizeDelta.x - 311) > 1 || Math.Abs(rect.anchoredPosition.y + 211) > 1)
            throw new InvalidOperationException("Unreviewed native GM stat geometry.");
        card.Originals = new GameObject[rows.Length]; card.Visible = new bool[rows.Length];
        for (int i = 0; i < rows.Length; i++) { card.Originals[i] = rows[i].gameObject; card.Visible[i] = rows[i].gameObject.activeSelf; }
        var values = Stats(card.Offer.Candidate);
        for (int i = 0; i < values.Length; i++)
        {
            bool fraction = i == values.Length - 1;
            int column = fraction ? 0 : i / 6, row = fraction ? 6 : i % 6;
            float x = rect.anchoredPosition.x + column * 161, y = rect.anchoredPosition.y - row * 15.5f;
            // Native Bungee digits need 48px for signed Skill; Pass Read gets
            // the full final row so its authored fractional precision survives.
            float labelWidth = i == 0 ? 100 : 114, numberWidth = fraction ? 195 : i == 0 ? 48 : 34;
            AddText(card, nameLabel, rect.parent, values[i].Name, x, y, labelWidth, false);
            AddText(card, template.m_MainValue, rect.parent, values[i].Value, x + labelWidth + 2, y, numberWidth, true);
        }
    }
    private static void AddText(Card card, TMP_Text template, Transform parent, string value, float x, float y, float width, bool number)
    {
        var text = UnityEngine.Object.Instantiate(template, parent, false);
        card.Text.Add(text);
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, 20);
        text.enableWordWrapping = false; text.enableAutoSizing = true;
        text.fontSizeMin = 14; text.fontSizeMax = 14; text.fontSize = 14;
        text.maxVisibleLines = 1; text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = number ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false; text.text = value; text.gameObject.SetActive(true);
    }
    private static void Remove(Card card)
    {
        // A pooled item already refreshed for another skater owns its new
        // visibility. Never restore the old goalie's flags over that refresh.
        bool same = card.Item != null && card.Item.ChoiceData.Skater?.Pointer == card.Candidate
            && card.Item.m_SkaterDetails?.Pointer == card.Details;
        if (same && card.Originals != null)
            for (int i = 0; i < card.Originals.Length; i++) if (card.Originals[i] != null) card.Originals[i].SetActive(card.Visible[i]);
        foreach (var text in card.Text) if (text != null) { text.gameObject.SetActive(false); UnityEngine.Object.Destroy(text.gameObject); }
        card.Text.Clear();
    }
}
