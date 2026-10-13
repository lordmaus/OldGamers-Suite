using System.Windows.Automation.Peers;

namespace TimerStudio
{
    /// <summary>
    /// Custom AutomationPeer for TimerCard.
    /// Exposes ControlType=Group and a meaningful Name (the timer's name + state)
    /// so screen readers can announce each card correctly.
    /// Default UserControlAutomationPeer reports ControlType=Custom which gives
    /// AT software no semantic hint about what the card represents.
    /// </summary>
    public class TimerCardAutomationPeer : UserControlAutomationPeer
    {
        public TimerCardAutomationPeer(TimerCard owner) : base(owner) { }

        protected override string GetClassNameCore() => "TimerCard";

        // GetControlTypeCore() is sealed in FrameworkElementAutomationPeer in .NET 10 WPF.
        // ControlType remains Custom (the UserControl default). Name and HelpText below
        // are what screen readers and Accessibility Insights actually surface.

        /// <summary>Returns the timer's display name as the accessible name.</summary>
        protected override string GetNameCore()
        {
            var card = (TimerCard)Owner;
            return card.ViewModel?.Name ?? base.GetNameCore();
        }

        /// <summary>Returns current state + time as the help text for AT users.</summary>
        protected override string GetHelpTextCore()
        {
            var card = (TimerCard)Owner;
            var vm = card.ViewModel;
            if (vm == null) return base.GetHelpTextCore();
            return $"Timer: {vm.Name}. State: {vm.State}. Time: {vm.DisplayTime}.";
        }
    }
}
