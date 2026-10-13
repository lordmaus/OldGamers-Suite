using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace TimerStudio
{
    /// <summary>
    /// Custom AutomationPeer for LensPopupBase (shared by LensAlertWindow and LensReminderWindow).
    /// Surfaces the window-level AutomationProperties.Name set via SetWindowName()
    /// so AT tools announce "Timer Alert: {name}" when a lens popup appears.
    /// </summary>
    public class LensPopupAutomationPeer : WindowAutomationPeer
    {
        public LensPopupAutomationPeer(LensPopupBase owner) : base(owner) { }

        protected override string GetClassNameCore() => "LensPopup";

        // GetControlTypeCore() is sealed in WindowAutomationPeer — Window type is correct as-is.

        protected override string GetNameCore()
        {
            string name = AutomationProperties.GetName(Owner);
            return !string.IsNullOrEmpty(name) ? name : base.GetNameCore();
        }

        protected override string GetHelpTextCore()
        {
            string help = AutomationProperties.GetHelpText(Owner);
            return !string.IsNullOrEmpty(help) ? help : base.GetHelpTextCore();
        }
    }
}
