using System;
using System.Threading.Tasks;
using BishopGames.Catalogs;
using BishopGames.Module;
using BishopGames.UI;
using STS.Map;
using Tape2Tape.Customization.UI;
using Result = Tape2Tape.Customization.UI.PromptTemplateMenu.PromptTemplateResult;

namespace EndlessMode;

// The game's prompt owns mouse, controller and Back input while it is on top.
// Keep one owner until its native result arrives, including during menu teardown.
internal sealed class SigningSwapPrompt
{
    private static SigningSwapPrompt owner;
    private CustomizableYesNoPrompt dialog;
    private bool abandoned;
    internal bool Pending => ReferenceEquals(owner, this);
    internal static string Name(string fullName, string id) => string.IsNullOrWhiteSpace(fullName) ? id : fullName;

    internal void Show(string description, Action<bool> answered)
    {
        if (Pending) return;
        if (abandoned || owner != null) { answered(false); return; }
        owner = this;
        _ = ShowAsync(description, answered);
    }

    private async Task ShowAsync(string description, Action<bool> answered)
    {
        bool approved = false;
        try
        {
            var data = new CustomizableYesNoPrompt.Data("Confirm swap", description, "Confirm swap", "Cancel",
                false, new Il2CppSystem.Nullable<Result>(Result.No), false);
            dialog = await BishopModule.Get<UIModule>().Cast<T2TUIModule>()
                .ShowMenuAsync<CustomizableYesNoPrompt>(MenuDefinitionCatalogReference.CustomizableYesNoPrompt, data);
            if (dialog == null) throw new InvalidOperationException("The signing confirmation did not open.");
            if (abandoned) Dismiss();
            approved = await dialog.WaitForResult() == Result.Yes;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("[Signing] Could not obtain a swap decision: " + ex);
            Dismiss();
        }
        finally
        {
            dialog = null;
            owner = null;
        }
        if (!abandoned) answered(approved);
    }

    internal void Close()
    {
        abandoned = true;
        Dismiss();
    }

    private void Dismiss()
    {
        try
        {
            if (dialog != null && !dialog.IsHiding && !dialog.m_HasSentResult) dialog.CloseWithResult(Result.Cancel);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[Signing] Could not close the confirmation: " + ex.Message); }
    }
}

internal static class LegacySigningOffers
{
    internal static void Restore(EventChoicePanel panel, EventNode node)
    {
        // The original legacy button handler clears its buttons before HideMenu
        // hands us the pending recruit. Reuse the same offers after cancellation.
        panel.ClearChoicesButtons();
        var list = panel.choicesList;
        int firstNewButton = list == null ? 0 : list.childCount;
        panel.InitializeChoicesButtons(node.eventChoices);
        // Unity can retain the destroyed buttons until the end of this frame.
        for (int i = firstNewButton; list != null && i < list.childCount; i++)
        {
            var button = list.GetChild(i).GetComponent<UnityEngine.UI.Button>();
            if (button == null || !button.interactable) continue;
            button.Select();
            break;
        }
    }
}
