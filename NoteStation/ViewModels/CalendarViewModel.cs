using System;
using System.Collections.Generic;
using System.Linq;

namespace NoteStation
{
    /// <summary>
    /// First slice of the MVVM extraction called for in the 2026-09-07 review
    /// (Architecture flag: MainWindow.xaml.cs is a 5,387-line god object with an
    /// empty ViewModels/ folder, and Calendar/Focus Mode/Matrix were named as the
    /// newest, most self-contained surfaces to start with). Pulls the Calendar
    /// view's pure state and logic — which month is showing, which tasks land on
    /// which day — out of MainWindow.xaml.cs's code-behind and into something
    /// independently testable.
    ///
    /// Deliberately NOT extracted yet: the imperative Border/StackPanel grid
    /// construction in MainWindow.BuildCalendar() (the actual visual tree for the
    /// 42-cell month grid). Converting that to XAML ItemsControl/DataTemplate
    /// bindings is a much larger change that can only be verified by looking at
    /// the running app, and this review pass has no dotnet toolchain to build it
    /// with — see Phase 4 of the review's improvement plan. Moving the surrounding
    /// state/logic here first, without touching the visual construction, keeps
    /// this slice low-risk while still making real progress on the god-object flag.
    /// </summary>
    public class CalendarViewModel
    {
        /// <summary>Always the 1st of the displayed month.</summary>
        public DateTime CurrentMonth { get; private set; }

        public CalendarViewModel(DateTime? initialMonth = null)
        {
            var d = (initialMonth ?? DateTime.Today).Date;
            CurrentMonth = new DateTime(d.Year, d.Month, 1);
        }

        public void GoToPreviousMonth() => CurrentMonth = CurrentMonth.AddMonths(-1);
        public void GoToNextMonth()     => CurrentMonth = CurrentMonth.AddMonths(1);

        /// <summary>Monday-based column offset of the 1st of <see cref="CurrentMonth"/> (0 = Monday).</summary>
        public int StartOffset => ((int)CurrentMonth.DayOfWeek + 6) % 7;

        public int DaysInMonth => DateTime.DaysInMonth(CurrentMonth.Year, CurrentMonth.Month);

        /// <summary>The calendar date for a 1-based day-of-month index.</summary>
        public DateTime DateForDay(int dayOfMonth) => new DateTime(CurrentMonth.Year, CurrentMonth.Month, dayOfMonth);

        /// <summary>Incomplete, non-archived tasks due on <paramref name="date"/>.</summary>
        public static List<TaskItem> GetTasksForDate(IEnumerable<TaskItem> allTasks, DateTime date)
            => allTasks.Where(t => !t.IsCompleted && !t.IsArchived &&
                                    t.HasDueDate && t.DueDate?.Date == date.Date)
                       .ToList();
    }
}
