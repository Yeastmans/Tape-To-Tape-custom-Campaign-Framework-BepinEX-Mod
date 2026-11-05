using BishopGames.Catalogs;
using BishopGames.UI;
using System.Runtime.CompilerServices;
using Tape2Tape.Customization.UI;
using static Tape2Tape.Customization.UI.PromptTemplateMenu;

internal static class SigningDialogFixture
{
    internal static CustomizableYesNoPrompt Current;
}

public partial class EventChoicePanel
{
    public sealed class ChoiceChild
    {
        public STS.Map.EventChoice Offer;
        public UnityEngine.UI.Button Button = new();
        public bool Retired;
        public T GetComponent<T>() where T : class => Button as T;
    }
    public sealed class ChoiceList : List<ChoiceChild>
    {
        public int childCount => Count;
        public ChoiceChild GetChild(int index) => this[index];
    }
    public ChoiceList choicesList = new();
    public void ClearChoicesButtons()
    {
        // Native destruction is deferred until the frame ends.
        foreach (var child in choicesList) child.Retired = true;
    }
    public void AddVisibleButtons(IEnumerable<STS.Map.EventChoice> offers)
    {
        foreach (var offer in offers) choicesList.Add(new() { Offer = offer, Button = new() { interactable = offer.interactable } });
    }
}
namespace UnityEngine.UI
{
    public class Button
    {
        public bool interactable, Focused;
        public void Select() => Focused = true;
    }
}

namespace Il2CppSystem
{
    public sealed class Nullable<T> where T : struct
    {
        public readonly T Value;
        public Nullable(T value) { Value = value; }
    }
}
namespace Cysharp.Threading.Tasks
{
    public class UniTask<T>
    {
        public T Value;
        internal bool Pending;
        internal System.Action Completed;
        public Awaiter GetAwaiter() => new() { Task = this };
        public struct Awaiter : INotifyCompletion
        {
            internal UniTask<T> Task;
            public bool IsCompleted => !Task.Pending;
            public T GetResult() => Task.Value;
            public void OnCompleted(System.Action action) => Task.Completed += action;
        }
    }
}
namespace BishopGames.Catalogs
{
    public partial class MenuDefinitionCatalogReference
    {
        public static MenuDefinitionCatalogReference CustomizableYesNoPrompt = new();
    }
}
namespace Tape2Tape.Customization.UI
{
    public class PromptTemplateMenu : AbstractMenu
    {
        public enum PromptTemplateResult { Yes, No, Cancel }
    }
    public class CustomizableYesNoPrompt : PromptTemplateMenu
    {
        public class Data
        {
            public string Title, Description, Yes, No;
            public PromptTemplateResult DefaultSelection;
            public Data(string title,string desc,string yes,string no,bool showCloseButton,
                Il2CppSystem.Nullable<PromptTemplateResult> defaultSelection,bool translateFromKeys)
            {
                Title=title; Description=desc; Yes=yes; No=no; DefaultSelection=defaultSelection.Value;
            }
        }
        internal Data Metadata;
        private readonly Cysharp.Threading.Tasks.UniTask<PromptTemplateResult> result = new() { Pending = true };
        public bool m_HasSentResult;
        public Cysharp.Threading.Tasks.UniTask<PromptTemplateResult> WaitForResult() => result;
        public void CloseWithResult(PromptTemplateResult choice)
        {
            if (m_HasSentResult) return;
            m_HasSentResult=true; IsHiding=true;
            if (ReferenceEquals(SigningDialogFixture.Current,this)) SigningDialogFixture.Current=null;
            result.Value=choice; result.Pending=false; result.Completed?.Invoke();
        }
    }
    public partial class T2TUIModule
    {
        public Cysharp.Threading.Tasks.UniTask<T> ShowMenuAsync<T>(MenuDefinitionCatalogReference reference,CustomizableYesNoPrompt.Data data) where T : class
        {
            if (FailOpening) throw new InvalidOperationException("Native prompt failed to open");
            if (reference!=MenuDefinitionCatalogReference.CustomizableYesNoPrompt) throw new InvalidOperationException("Unexpected native menu");
            var dialog=SigningDialogFixture.Current=new CustomizableYesNoPrompt { Metadata=data };
            return new() { Value=dialog as T };
        }
    }
}
