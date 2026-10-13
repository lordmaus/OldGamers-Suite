using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace TimerStudio
{
    /// <summary>
    /// Custom AutomationPeer for FlashOverlayWindow.
    /// Surfaces the window-level AutomationProperties.Name set in the constructor
    /// so AT tools announce "Timer Alert: {name}" when the fullscreen overlay appears.
    /// </summary>
    public class FlashOverlayWindowAutomationPeer : WindowAutomationPeer
    {
        public FlashOverlayWindowAutomationPeer(FlashOverlayWindow owner) : base(owner) { }

        protected override string GetClassNameCore() => "FlashOverlayWindow";

        // GetControlTypeCore() is sealed in WindowAutomationPeer — Window type is correct as-is.

        protected override string GetNameCore()
        {
            // Prefer the explicitly-set AutomationProperties.Name on the Window.
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
