using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using Ical.Net.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Task_Flyout.Services
{
    internal sealed record ICloudParsedEvent(
        string Id,
        string RecurringEventId,
        string Title,
        string Location,
        string Description,
        DateTime Start,
        DateTime End,
        bool IsAllDay,
        bool IsRecurring,
        string RecurrenceKind);

    internal static class ICloudIcsCodec
    {
        private const int MaxCalendarDataCharacters = 2 * 1024 * 1024;
        private const int MaxOccurrencesPerResource = 5000;
        private const int MaxEventComponentsPerResource = 5000;
        private static readonly TimeSpan MaxQueryRange = TimeSpan.FromDays(366 * 5);

        public static IReadOnlyList<ICloudParsedEvent> ParseEvents(
            string calendarData,
            Uri resourceUri,
            DateTime rangeStart,
            DateTime rangeEnd)
        {
            if (rangeEnd <= rangeStart || rangeEnd - rangeStart > MaxQueryRange)
                throw new ArgumentOutOfRangeException(nameof(rangeEnd), "The iCloud calendar query range is invalid or too large.");

            Calendar calendar = LoadCalendar(calendarData);
            DateTime startUtc = ToUtcBoundary(rangeStart);
            DateTime endUtc = ToUtcBoundary(rangeEnd);
            var evaluationOptions = new EvaluationOptions { MaxUnmatchedIncrementsLimit = 100_000 };
            var masters = calendar.Events
                .Where(calendarEvent => GetRecurrenceId(calendarEvent) == null && !string.IsNullOrWhiteSpace(calendarEvent.Uid))
                .Select(calendarEvent => (Uid: calendarEvent.Uid!, Event: calendarEvent))
                .GroupBy(value => value.Uid, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Event, StringComparer.Ordinal);
            var result = new List<ICloudParsedEvent>();

            try
            {
                var occurrences = calendar
                    .GetOccurrences<CalendarEvent>(new CalDateTime(startUtc, true), evaluationOptions)
                    .TakeWhileBefore(new CalDateTime(endUtc, true));
                int enumeratedOccurrences = 0;

                foreach (var occurrence in occurrences)
                {
                    enumeratedOccurrences++;
                    if (enumeratedOccurrences > MaxOccurrencesPerResource)
                        throw new InvalidDataException("An iCloud calendar resource contains too many event occurrences.");
                    if (occurrence.Source is not CalendarEvent source || string.IsNullOrWhiteSpace(source.Uid))
                        continue;
                    if (string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                        continue;

                    masters.TryGetValue(source.Uid, out var master);
                    var recurrenceRule = master?.RecurrenceRule ?? source.RecurrenceRule;
                    bool isRecurring = HasRecurrence(master ?? source) || GetRecurrenceId(source) != null;
                    CalDateTime occurrenceStart = occurrence.Period.StartTime;
                    CalDateTime occurrenceEnd = occurrence.Period.EffectiveEndTime ?? occurrenceStart;
                    CalDateTime recurrenceKey = GetRecurrenceId(source) ?? occurrenceStart;
                    bool isAllDay = source.IsAllDay || !occurrenceStart.HasTime;
                    bool referenceIsAllDay = !recurrenceKey.HasTime;
                    DateTime displayStart = ToDisplayDateTime(occurrenceStart, isAllDay);
                    DateTime displayEnd = ToDisplayDateTime(occurrenceEnd, isAllDay);
                    if (displayEnd < displayStart) displayEnd = displayStart;

                    var reference = new ICloudEventReference(
                        resourceUri,
                        source.Uid,
                        isRecurring,
                        referenceIsAllDay,
                        DateTime.SpecifyKind(recurrenceKey.Value, DateTimeKind.Unspecified),
                        recurrenceKey.TzId ?? "");
                    string id = ICloudEventReferenceCodec.Encode(reference);

                    result.Add(new ICloudParsedEvent(
                        id,
                        isRecurring ? id : "",
                        source.Summary ?? master?.Summary ?? "",
                        source.Location ?? master?.Location ?? "",
                        source.Description ?? master?.Description ?? "",
                        displayStart,
                        displayEnd,
                        isAllDay,
                        isRecurring,
                        ToRecurrenceKind(recurrenceRule?.Frequency)));
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("The iCloud recurrence data could not be evaluated safely.", ex);
            }

            return result;
        }

        public static string CreateCalendar(
            string uid,
            string title,
            string location,
            string description,
            DateTime start,
            DateTime end,
            bool isAllDay,
            string recurrenceKind,
            string? timeZoneId = null)
        {
            var calendar = CreateEmptyCalendar();
            FrequencyType? frequency = ParseFrequency(recurrenceKind);
            string recurrenceTimeZoneId = frequency.HasValue && !isAllDay
                ? NormalizeTimeZoneId(timeZoneId ?? GetLocalTimeZoneId())
                : "";
            if (!string.IsNullOrWhiteSpace(recurrenceTimeZoneId)
                && !string.Equals(recurrenceTimeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
            {
                calendar.AddTimeZone(recurrenceTimeZoneId);
            }

            var calendarEvent = new CalendarEvent
            {
                Uid = uid,
                Summary = title,
                Location = string.IsNullOrWhiteSpace(location) ? null : location,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                DtStamp = new CalDateTime(DateTime.UtcNow, true)
            };
            ApplyEventTime(calendarEvent, start, end, isAllDay, recurrenceTimeZoneId);

            if (frequency.HasValue)
                calendarEvent.RecurrenceRule = new RecurrenceRule(frequency.Value);

            calendar.Events.Add(calendarEvent);
            return Serialize(calendar);
        }

        public static string UpdateCalendar(
            string calendarData,
            ICloudEventReference reference,
            string title,
            string location,
            string description,
            DateTime start,
            DateTime end,
            bool isAllDay)
        {
            Calendar calendar = LoadCalendar(calendarData);
            CalendarEvent master = FindMaster(calendar, reference.Uid);

            if (!reference.IsRecurring)
            {
                EnsureCalendarTimeZone(calendar, reference.TimeZoneId);
                ApplyEventFields(master, title, location, description, start, end, isAllDay, reference.TimeZoneId);
                master.Sequence++;
                master.DtStamp = new CalDateTime(DateTime.UtcNow, true);
                return Serialize(calendar);
            }

            int previousSequence = RemoveMatchingOverrides(calendar, reference);
            EnsureCalendarTimeZone(calendar, reference.TimeZoneId);
            var occurrenceOverride = new CalendarEvent
            {
                Uid = master.Uid,
                RecurrenceIdentifier = new RecurrenceIdentifier(CreateRecurrenceId(reference), null),
                Summary = title,
                Location = string.IsNullOrWhiteSpace(location) ? null : location,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                Sequence = Math.Max(master.Sequence, previousSequence) + 1,
                DtStamp = new CalDateTime(DateTime.UtcNow, true)
            };
            ApplyEventTime(occurrenceOverride, start, end, isAllDay, reference.TimeZoneId);
            calendar.Events.Add(occurrenceOverride);
            return Serialize(calendar);
        }

        public static string DeleteOccurrence(
            string calendarData,
            ICloudEventReference reference,
            RecurringDeleteMode deleteMode)
        {
            if (!reference.IsRecurring)
                throw new InvalidOperationException("The selected iCloud event is not recurring.");

            Calendar calendar = LoadCalendar(calendarData);
            CalendarEvent master = FindMaster(calendar, reference.Uid);
            RemoveMatchingOverrides(calendar, reference);

            if (deleteMode == RecurringDeleteMode.Single)
            {
                master.ExceptionDates.Add(CreateRecurrenceId(reference));
            }
            else if (deleteMode == RecurringDeleteMode.ThisAndFollowing)
            {
                if (master.RecurrenceRule != null)
                {
                    master.RecurrenceRule.Until = CreateUntilBefore(reference);
                    master.RecurrenceRule.Count = null;
                }
                else if (!HasRecurrenceDates(master))
                {
                    throw new InvalidOperationException("The iCloud recurrence rule cannot be shortened.");
                }
                else
                {
                    master.ExceptionDates.Add(CreateRecurrenceId(reference));
                }

                RemoveRecurrenceDatesAtOrAfter(master, reference);
                RemoveOverridesAtOrAfter(calendar, reference);
            }
            else
            {
                throw new InvalidOperationException("Deleting the complete series must remove its CalDAV resource.");
            }

            master.Sequence++;
            master.DtStamp = new CalDateTime(DateTime.UtcNow, true);
            return Serialize(calendar);
        }

        private static Calendar LoadCalendar(string calendarData)
        {
            if (string.IsNullOrWhiteSpace(calendarData) || calendarData.Length > MaxCalendarDataCharacters)
                throw new InvalidDataException("The iCloud calendar data was empty or too large.");
            try
            {
                Calendar calendar = Calendar.Load(calendarData)
                    ?? throw new InvalidDataException("The iCloud calendar data could not be parsed.");
                ValidateCalendar(calendar);
                return calendar;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("The iCloud calendar data could not be parsed.", ex);
            }
        }

        private static Calendar CreateEmptyCalendar()
            => new()
            {
                ProductId = "-//Task Flyout//iCloud Calendar//EN",
                Version = "2.0",
                Scale = "GREGORIAN"
            };

        private static CalendarEvent FindMaster(Calendar calendar, string uid)
            => calendar.Events.FirstOrDefault(calendarEvent =>
                    string.Equals(calendarEvent.Uid, uid, StringComparison.Ordinal)
                    && GetRecurrenceId(calendarEvent) == null)
                ?? throw new InvalidDataException("The iCloud event no longer exists in its calendar resource.");

        private static void ApplyEventFields(
            CalendarEvent calendarEvent,
            string title,
            string location,
            string description,
            DateTime start,
            DateTime end,
            bool isAllDay,
            string? timeZoneId)
        {
            calendarEvent.Summary = title;
            calendarEvent.Location = string.IsNullOrWhiteSpace(location) ? null : location;
            calendarEvent.Description = string.IsNullOrWhiteSpace(description) ? null : description;
            ApplyEventTime(calendarEvent, start, end, isAllDay, timeZoneId);
        }

        private static void ApplyEventTime(
            CalendarEvent calendarEvent,
            DateTime start,
            DateTime end,
            bool isAllDay,
            string? timeZoneId)
        {
            if (isAllDay)
            {
                DateOnly startDate = DateOnly.FromDateTime(start);
                DateOnly endDate = DateOnly.FromDateTime(end);
                if (endDate <= startDate) endDate = startDate.AddDays(1);
                calendarEvent.DtStart = new CalDateTime(startDate);
                calendarEvent.DtEnd = new CalDateTime(endDate);
            }
            else
            {
                if (end <= start) end = start.AddHours(1);
                string normalizedTimeZoneId = NormalizeTimeZoneId(timeZoneId);
                if (string.IsNullOrWhiteSpace(normalizedTimeZoneId))
                {
                    calendarEvent.DtStart = new CalDateTime(ToUtcBoundary(start), true);
                    calendarEvent.DtEnd = new CalDateTime(ToUtcBoundary(end), true);
                }
                else if (string.Equals(normalizedTimeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
                {
                    calendarEvent.DtStart = new CalDateTime(ToUtcBoundary(start), true);
                    calendarEvent.DtEnd = new CalDateTime(ToUtcBoundary(end), true);
                }
                else
                {
                    calendarEvent.DtStart = new CalDateTime(
                        DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
                        normalizedTimeZoneId,
                        true);
                    calendarEvent.DtEnd = new CalDateTime(
                        DateTime.SpecifyKind(end, DateTimeKind.Unspecified),
                        normalizedTimeZoneId,
                        true);
                }
            }
        }

        private static CalDateTime CreateRecurrenceId(ICloudEventReference reference)
        {
            if (reference.IsAllDay)
                return new CalDateTime(DateOnly.FromDateTime(reference.OccurrenceStart));

            string timeZoneId = NormalizeTimeZoneId(reference.TimeZoneId);
            DateTime value = DateTime.SpecifyKind(reference.OccurrenceStart, DateTimeKind.Unspecified);
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return new CalDateTime(value);
            if (string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
                return new CalDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc), true);
            return new CalDateTime(value, timeZoneId, true);
        }

        private static CalDateTime CreateUntilBefore(ICloudEventReference reference)
        {
            if (reference.IsAllDay)
                return new CalDateTime(DateOnly.FromDateTime(reference.OccurrenceStart.AddDays(-1)));

            CalDateTime recurrenceId = CreateRecurrenceId(reference);
            if (string.IsNullOrWhiteSpace(recurrenceId.TzId))
                return new CalDateTime(reference.OccurrenceStart.AddSeconds(-1));
            return new CalDateTime(recurrenceId.AsUtc.AddSeconds(-1), true);
        }

        private static int RemoveMatchingOverrides(Calendar calendar, ICloudEventReference reference)
        {
            var matches = calendar.Events
                .Where(calendarEvent => string.Equals(calendarEvent.Uid, reference.Uid, StringComparison.Ordinal)
                    && GetRecurrenceId(calendarEvent) is { } recurrenceId
                    && SameOccurrence(recurrenceId, reference))
                .ToList();
            int maxSequence = matches.Select(calendarEvent => calendarEvent.Sequence).DefaultIfEmpty(0).Max();
            foreach (var calendarEvent in matches)
            {
                RemoveCalendarEventByReference(calendar, calendarEvent);
            }
            return maxSequence;
        }

        private static void RemoveOverridesAtOrAfter(Calendar calendar, ICloudEventReference reference)
        {
            foreach (var calendarEvent in calendar.Events
                         .Where(calendarEvent => string.Equals(calendarEvent.Uid, reference.Uid, StringComparison.Ordinal)
                             && GetRecurrenceId(calendarEvent) is { } recurrenceId
                             && IsAtOrAfter(recurrenceId, reference))
                         .ToList())
            {
                RemoveCalendarEventByReference(calendar, calendarEvent);
            }
        }

        private static void RemoveCalendarEventByReference(Calendar calendar, CalendarEvent target)
        {
            var children = (IList<ICalendarObject>)calendar.Children;
            for (int index = children.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(children[index], target))
                {
                    children.RemoveAt(index);
                    return;
                }
            }

            throw new InvalidDataException("The iCloud recurrence override could not be removed safely.");
        }

        private static void RemoveRecurrenceDatesAtOrAfter(
            CalendarEvent master,
            ICloudEventReference reference)
        {
            foreach (var recurrenceDate in master.RecurrenceDates.GetAllDates()
                         .Where(value => IsAtOrAfter(value, reference))
                         .ToList())
            {
                master.RecurrenceDates.Remove(recurrenceDate);
            }

            foreach (var period in master.RecurrenceDates.GetAllPeriods()
                         .Where(period => IsAtOrAfter(period.StartTime, reference))
                         .ToList())
            {
                master.RecurrenceDates.Remove(period);
            }
        }

        private static bool SameOccurrence(CalDateTime value, ICloudEventReference reference)
        {
            CalDateTime target = CreateRecurrenceId(reference);
            if (!value.HasTime || !target.HasTime)
                return !value.HasTime && !target.HasTime && value.Value.Date == target.Value.Date;

            bool valueIsFloating = string.IsNullOrWhiteSpace(value.TzId);
            bool targetIsFloating = string.IsNullOrWhiteSpace(target.TzId);
            if (valueIsFloating || targetIsFloating)
                return valueIsFloating && targetIsFloating && value.Value == target.Value;
            return value.AsUtc == target.AsUtc;
        }

        private static bool IsAtOrAfter(CalDateTime value, ICloudEventReference reference)
        {
            CalDateTime target = CreateRecurrenceId(reference);
            if (!value.HasTime || !target.HasTime)
                return value.Value.Date >= target.Value.Date;

            bool valueIsFloating = string.IsNullOrWhiteSpace(value.TzId);
            bool targetIsFloating = string.IsNullOrWhiteSpace(target.TzId);
            if (valueIsFloating || targetIsFloating)
                return value.Value >= target.Value;
            return value.AsUtc >= target.AsUtc;
        }

        private static bool HasRecurrence(CalendarEvent calendarEvent)
            => calendarEvent.RecurrenceRule != null || HasRecurrenceDates(calendarEvent);

        private static bool HasRecurrenceDates(CalendarEvent calendarEvent)
            => calendarEvent.RecurrenceDates.GetAllDates().Any()
                || calendarEvent.RecurrenceDates.GetAllPeriods().Any();

        private static CalDateTime? GetRecurrenceId(CalendarEvent calendarEvent)
            => calendarEvent.RecurrenceIdentifier?.StartTime;

        private static void ValidateCalendar(Calendar calendar)
        {
            var calendarEvents = calendar.Events.ToList();
            if (calendarEvents.Count > MaxEventComponentsPerResource)
                throw new InvalidDataException("An iCloud calendar resource contains too many event components.");

            foreach (var calendarEvent in calendarEvents)
            {
                if (string.IsNullOrWhiteSpace(calendarEvent.Uid)
                    || calendarEvent.Uid.Length > ICloudEventReferenceCodec.MaxUidLength)
                {
                    throw new InvalidDataException("An iCloud event identifier is missing or too large.");
                }

                ValidateTimeZoneId(calendarEvent.DtStart);
                ValidateTimeZoneId(calendarEvent.DtEnd);
                ValidateTimeZoneId(GetRecurrenceId(calendarEvent));
                foreach (var exceptionDate in calendarEvent.ExceptionDates.GetAllDates())
                    ValidateTimeZoneId(exceptionDate);
                foreach (var recurrenceDate in calendarEvent.RecurrenceDates.GetAllPeriods())
                {
                    ValidateTimeZoneId(recurrenceDate.StartTime);
                    ValidateTimeZoneId(recurrenceDate.EndTime);
                }
                foreach (var recurrenceDate in calendarEvent.RecurrenceDates.GetAllDates())
                    ValidateTimeZoneId(recurrenceDate);
                ValidateTimeZoneId(calendarEvent.RecurrenceRule?.Until);
            }

            if (calendarEvents.Select(calendarEvent => calendarEvent.Uid).Distinct(StringComparer.Ordinal).Skip(1).Any())
                throw new InvalidDataException("A CalDAV calendar resource contains more than one event identifier.");
        }

        private static void ValidateTimeZoneId(CalDateTime? value)
        {
            if ((value?.TzId?.Length ?? 0) > ICloudEventReferenceCodec.MaxTimeZoneIdLength)
                throw new InvalidDataException("An iCloud event time-zone identifier is too large.");
        }

        private static void EnsureCalendarTimeZone(Calendar calendar, string? timeZoneId)
        {
            string normalized = NormalizeTimeZoneId(timeZoneId);
            if (string.IsNullOrWhiteSpace(normalized)
                || string.Equals(normalized, "UTC", StringComparison.OrdinalIgnoreCase)
                || calendar.TimeZones.Any(zone => string.Equals(zone.TzId, normalized, StringComparison.Ordinal)))
            {
                return;
            }

            calendar.AddTimeZone(normalized);
        }

        private static string NormalizeTimeZoneId(string? timeZoneId)
        {
            string normalized = timeZoneId?.Trim() ?? "";
            if (normalized.Length > ICloudEventReferenceCodec.MaxTimeZoneIdLength)
                throw new InvalidDataException("An iCloud event time-zone identifier is too large.");
            return normalized;
        }

        private static string GetLocalTimeZoneId()
        {
            string localId = TimeZoneInfo.Local.Id;
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(localId, out string? ianaId)
                && !string.IsNullOrWhiteSpace(ianaId))
            {
                return ianaId;
            }
            return localId;
        }

        private static DateTime ToDisplayDateTime(CalDateTime value, bool isAllDay)
            => isAllDay
                ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Unspecified)
                : value.AsUtc.ToLocalTime();

        private static DateTime ToUtcBoundary(DateTime value)
            => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            };

        private static string Serialize(Calendar calendar)
            => new CalendarSerializer(calendar).SerializeToString()
                ?? throw new InvalidDataException("The iCloud calendar data could not be serialized.");

        private static string ToRecurrenceKind(FrequencyType? frequency)
            => frequency switch
            {
                FrequencyType.Daily => "Daily",
                FrequencyType.Weekly => "Weekly",
                FrequencyType.Monthly => "Monthly",
                FrequencyType.Yearly => "Yearly",
                _ => "None"
            };

        private static FrequencyType? ParseFrequency(string? recurrenceKind)
            => recurrenceKind?.Trim().ToLowerInvariant() switch
            {
                "daily" => FrequencyType.Daily,
                "weekly" => FrequencyType.Weekly,
                "monthly" => FrequencyType.Monthly,
                "yearly" => FrequencyType.Yearly,
                _ => null
            };
    }
}
