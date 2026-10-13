using System;
using NoteStation.Core;
using Xunit;

namespace NoteStation.Tests
{
    public class NlpTaskParserTests
    {
        private static readonly DateTime Today = new(2026, 10, 12); // Monday

        [Theory]
        [InlineData("call mom in 99999999999 days")]   // int overflow (was OverflowException)
        [InlineData("call mom in 3000000 days")]       // AddDays out of range (was ArgumentOutOfRangeException)
        [InlineData("call mom in 9999 months")]
        [InlineData("call mom in 3651 days")]
        public void Out_of_range_offset_keeps_phrase_and_reports_it(string input)
        {
            var r = NlpTaskParser.Parse(input, Today);
            Assert.Null(r.DueDate);
            Assert.Equal(input, r.CleanTitle);
            Assert.NotNull(r.UnparsedDatePhrase);
            Assert.StartsWith("in ", r.UnparsedDatePhrase);
        }

        [Theory]
        [InlineData("pay rent in 3 days", 3)]
        [InlineData("pay rent in 2 weeks", 14)]
        [InlineData("pay rent in 3650 days", 3650)]
        public void In_range_offset_parses(string input, int days)
        {
            var r = NlpTaskParser.Parse(input, Today);
            Assert.Equal(Today.AddDays(days), r.DueDate);
            Assert.Equal("pay rent", r.CleanTitle);
            Assert.Null(r.UnparsedDatePhrase);
        }

        [Fact]
        public void Months_offset_parses()
            => Assert.Equal(Today.AddMonths(120), NlpTaskParser.Parse("x in 120 months", Today).DueDate);

        [Theory]
        [InlineData("dentist Apr 31")]
        [InlineData("dentist 31 April")]
        [InlineData("dentist Feb 30")]
        public void Invalid_calendar_date_is_reported_not_dropped(string input)
        {
            var r = NlpTaskParser.Parse(input, Today);
            Assert.Null(r.DueDate);
            Assert.Equal(input, r.CleanTitle);
            Assert.NotNull(r.UnparsedDatePhrase);
        }

        [Fact]
        public void Month_day_rolls_to_next_year_when_past()
            => Assert.Equal(new DateTime(2027, 4, 5), NlpTaskParser.Parse("x Apr 5", Today).DueDate);

        [Fact]
        public void Feb_29_in_leap_year_is_valid()
            => Assert.Equal(new DateTime(2028, 2, 29), NlpTaskParser.Parse("x Feb 29", new DateTime(2027, 12, 1)).DueDate);

        [Fact]
        public void Full_sentence_extracts_all_fields()
        {
            var r = NlpTaskParser.Parse("Buy GPU ram tomorrow 5pm !high every week", Today);
            Assert.Equal("Buy GPU ram", r.CleanTitle);
            Assert.Equal(Today.AddDays(1), r.DueDate);
            Assert.Equal(new TimeSpan(17, 0, 0), r.DueTime);
            Assert.Equal(3, r.Priority);
            Assert.Equal("Weekly", r.Recurrence);
        }

        [Fact]
        public void Bare_weekday_is_next_occurrence_never_today()
            => Assert.Equal(Today.AddDays(7), NlpTaskParser.Parse("standup monday", Today).DueDate);

        [Fact]
        public void TryOffset_rejects_five_digits_without_parsing()
            => Assert.False(NlpTaskParser.TryOffset(Today, "12345", "day", out _));
    }
}
