namespace Uberkarl.Editor.Input;

/// <summary>Reconciles a trigger-driven <see cref="MenuSession.Step"/> transition with a same-frame surface-driven cancel/resolve request.</summary>
public static class MenuCloseArbitration
{
    public readonly struct Resolution
    {
        public Resolution(MenuSessionTransition transition, bool forceCancel)
        {
            Transition = transition;
            ForceCancel = forceCancel;
        }

        public MenuSessionTransition Transition { get; }

        public bool ForceCancel { get; }
    }

    public static Resolution Resolve(MenuSession session, MenuSessionTransition transition, bool cancelRequested, bool resolveRequested)
    {
        bool forceCancel = cancelRequested;
        if (transition.Effect == MenuSessionEffect.None && (cancelRequested || resolveRequested))
            transition = session.Resolve();
        return new Resolution(transition, forceCancel);
    }
}
