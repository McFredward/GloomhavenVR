using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UnityEngine.UI
{
 public partial class UIWindow : MonoBehaviour
 {
  public enum VisualState { Shown, Hidden }
  public enum Transition { Instant, Fade }
  public enum UIWindowID { None, Shop, EnhancementShop }
  public UIWindowID ID;
  private VisualState m_CurrentVisualState = VisualState.Hidden;
  private Transition m_Transition = Transition.Fade;
  private CanvasGroup m_CanvasGroup = null!;
  private string m_AudioItemShow = "", m_AudioItemHide = "";
  private EscapeKeyAction escapeKeyAction;
  public readonly UnityEvent onShown = new(), onHidden = new();
  public readonly UnityEvent<UIWindow,VisualState,bool> onTransitionBegin = new();
  public readonly UnityEvent<UIWindow,VisualState> onTransitionComplete = new();
  public Action? OnShow { get; set; }
  public Action? OnHide { get; set; }
  public Action<bool>? OnActivityChanged;
  public bool IsOpen => m_CurrentVisualState == VisualState.Shown;
  public bool IsVisible => m_CanvasGroup.alpha > 0f;
  public void Bind() { m_CanvasGroup = GetComponent<CanvasGroup>(); }
  private bool IsActive() => gameObject.activeInHierarchy;
  private void Focus() { }
  private void OnTransitionStarted(VisualState state,bool instant) { }
  private void OnTransitionCompleted() { }
  private bool pendingTween;
  public void StartAlphaTween(float target,float duration,bool ignoreTimeScale)
  {
   m_CanvasGroup.blocksRaycasts=false;pendingTween=duration>0f;
   // Installed TweenRunner.Start skips its while loop for zero duration, then
   // calls TweenValue(1) and Finished synchronously inside StartCoroutine.
   if(!pendingTween) {SetCanvasAlpha(target);OnTweenFinished();}
  }
  private float m_TransitionDuration = .2f;
  private void SetCanvasAlpha(float alpha) { m_CanvasGroup.alpha=alpha; if(alpha==0f) {m_CanvasGroup.blocksRaycasts=false;m_CanvasGroup.interactable=false;} }
  // A declared deterministic tween-completion port. Native OnTweenFinished below
  // owns the callback order. No fixture callback substitutes a game transaction.
  public void FinishTransition() { if(!pendingTween)return;pendingTween=false;SetCanvasAlpha(IsOpen ? 1f : 0f);OnTweenFinished(); }
  public void Show() => Show(false);
  public void Hide() => Hide(false);
 }
}
public partial class UIEnhancementConfirmationBox : MonoBehaviour
{
 private Image enhancementIcon = null!;
 private Label enhancementName = new(), titleText = new(), informationText = new();
 private ExtendedButton confirmButton = null!, cancelButton = null!;
 private ControllerInputAreaLocal controllerArea = new();
 private UIWindow _confirmationBox = null!;
 private Action? _onConfirmCallback;
 private SkipFrameKeyActionHandlerBlocker _skipFrameKeyActionHandlerBlocker = new();
 public ExtendedButton Confirm => confirmButton;
 public ExtendedButton Cancel => cancelButton;
 public ControllerInputAreaLocal Area => controllerArea;
 public void Bind(Image icon, ExtendedButton confirm, ExtendedButton cancel)
 {
  enhancementIcon=icon; confirmButton=confirm; cancelButton=cancel;
  _confirmationBox=GetComponent<UIWindow>();
  _confirmationBox.OnHide += ToPreviousState;
 }
}
