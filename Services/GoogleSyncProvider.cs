using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Task_Flyout.Services
{
    public class GoogleSyncProvider : ISyncProvider
    {
        public string ProviderName => "Google";
        public CalendarService? CalendarSvc { get; private set; }
        public TasksService? TasksSvc { get; private set; }
        private ResourceLoader _loader = new ResourceLoader();
        public async Task EnsureAuthorizedAsync()
        {
            if (CalendarSvc != null && TasksSvc != null) return;

            string[] scopes = { CalendarService.Scope.Calendar, TasksService.Scope.Tasks };
            string tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout", "GoogleToken");
            UserCredential credential;

            try
            {
                string credPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");
                using (var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read))
                {
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets, scopes, "user", CancellationToken.None, new FileDataStore(tokenPath, true));
                }
            }
            catch (FileNotFoundException)
            {
                throw new Exception(_loader.GetString("TextCredNotFound"));
            }

            CalendarSvc = new CalendarService(new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "Task Flyout" });
            TasksSvc = new TasksService(new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "Task Flyout" });
        }

        public async Task<List<SubscribedCalendarInfo>> FetchCalendarListAsync()
        {
            var result = new List<SubscribedCalendarInfo>();
            if (CalendarSvc == null) return result;

            try
            {
                var listRequest = CalendarSvc.CalendarList.List();
                var calendarList = await listRequest.ExecuteAsync();
                if (calendarList?.Items != null)
                {
                    foreach (var cal in calendarList.Items)
                    {
                        result.Add(new SubscribedCalendarInfo
                        {
                            Id = cal.Id,
                            Name = cal.SummaryOverride ?? cal.Summary ?? cal.Id,
                            IsVisible = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Google] Failed to fetch calendar list: {ex.Message}");
            }

            return result;
        }

        public async Task<List<AgendaItem>> FetchDataAsync(DateTime min, DateTime max)
        {
            var items = new List<AgendaItem>();

            // Fetch events from all calendars
            var calendars = await FetchCalendarListAsync();
            if (calendars.Count == 0)
            {
                // Fallback to primary if calendar list fails
                calendars.Add(new SubscribedCalendarInfo { Id = "primary", Name = "Primary" });
            }

            foreach (var cal in calendars)
            {
                string pageToken = null;
                do
                {
                    try
                    {
                        var req = CalendarSvc.Events.List(cal.Id);
                        req.TimeMinDateTimeOffset = min;
                        req.TimeMaxDateTimeOffset = max;
                        req.SingleEvents = true;
                        req.MaxResults = 2500;
                        req.PageToken = pageToken;
                        var events = await req.ExecuteAsync().ConfigureAwait(false);
                        if (events?.Items != null)
                        {
                            foreach (var ev in events.Items)
                            {
                                DateTime? date = ev.Start?.DateTime ?? (DateTime.TryParse(ev.Start?.Date, out var d) ? d : null);
                                if (date == null) continue;

                                items.Add(new AgendaItem
                                {
                                    Id = ev.Id,
                                    Title = ev.Summary,
                                    Subtitle = ev.Start?.DateTime == null ? _loader.GetString("TextAllDay") : ev.Start.DateTime.Value.ToString("HH:mm"),
                                    Location = ev.Location,
                                    Description = ev.Description,
                                    IsEvent = true,
                                    Provider = ProviderName,
                                    CalendarId = cal.Id,
                                    CalendarName = cal.Name,
                                    DateKey = date.Value.ToString("yyyy-MM-dd")
                                });
                            }
                        }
                        pageToken = events?.NextPageToken;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Google] Failed to fetch events from calendar {cal.Id}: {ex.Message}");
                        pageToken = null;
                    }
                } while (pageToken != null);
            }

            // Fetch tasks from @default list
            string taskPageToken = null;
            do
            {
                var tasksReq = TasksSvc.Tasks.List("@default");
                tasksReq.ShowHidden = true; tasksReq.MaxResults = 100; tasksReq.PageToken = taskPageToken;
                var tasks = await tasksReq.ExecuteAsync().ConfigureAwait(false);
                if (tasks?.Items != null)
                {
                    foreach (var t in tasks.Items)
                    {
                        bool isDone = t.Status == "completed";
                        DateTime taskDate = DateTime.Today;
                        if (!string.IsNullOrEmpty(t.Due) && DateTime.TryParse(t.Due, out var dueTime)) taskDate = dueTime.Date;
                        else if (isDone && !string.IsNullOrEmpty(t.Completed) && DateTime.TryParse(t.Completed, out var compTime)) taskDate = compTime.Date;
                        else if (isDone) continue;

                        items.Add(new AgendaItem
                        {
                            Id = t.Id,
                            Title = t.Title,
                            Subtitle = _loader.GetString("TextTask"),
                            IsEvent = false,
                            IsTask = true,
                            IsCompleted = isDone,
                            Description = t.Notes,
                            Provider = ProviderName,
                            DateKey = taskDate.ToString("yyyy-MM-dd")
                        });
                    }
                }
                taskPageToken = tasks?.NextPageToken;
            } while (taskPageToken != null);

            return items;
        }

        public async Task UpdateItemAsync(string itemId, bool isEvent, string title, string location, string description, DateTime targetDate, TimeSpan? startTime, TimeSpan? endTime)
        {
            if (isEvent)
            {
                var ev = await CalendarSvc.Events.Get("primary", itemId).ExecuteAsync();
                ev.Summary = title;
                ev.Location = location;
                ev.Description = description;

                if (startTime.HasValue)
                {
                    DateTime exactStart = targetDate.Add(startTime.Value);
                    DateTime exactEnd = endTime.HasValue ? targetDate.Add(endTime.Value) : exactStart.AddHours(1);
                    if (exactEnd < exactStart) exactEnd = exactEnd.AddDays(1);

                    ev.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = exactStart };
                    ev.End = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = exactEnd };
                }
                else
                {
                    ev.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = targetDate.ToString("yyyy-MM-dd") };
                    ev.End = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = targetDate.AddDays(1).ToString("yyyy-MM-dd") };
                }
                await CalendarSvc.Events.Update(ev, "primary", itemId).ExecuteAsync();
            }
            else
            {
                var task = await TasksSvc.Tasks.Get("@default", itemId).ExecuteAsync();
                task.Title = title;
                task.Notes = description;
                task.Due = targetDate.ToString("yyyy-MM-dd'T'00:00:00.000'Z'");
                await TasksSvc.Tasks.Update(task, "@default", itemId).ExecuteAsync();
            }
        }

        public async Task UpdateTaskStatusAsync(string taskId, bool isCompleted)
        {
            var status = isCompleted ? "completed" : "needsAction";
            var updateRequest = TasksSvc.Tasks.Patch(new Google.Apis.Tasks.v1.Data.Task { Id = taskId, Status = status }, "@default", taskId);
            await updateRequest.ExecuteAsync();
        }

        public async Task CreateEventAsync(string title, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, bool isAllDay)
        {
            var newEvent = new Google.Apis.Calendar.v3.Data.Event
            {
                Summary = title,
                Location = string.IsNullOrWhiteSpace(location) ? null : location
            };

            if (isAllDay)
            {
                newEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = targetDate.ToString("yyyy-MM-dd") };
                newEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = targetDate.AddDays(1).ToString("yyyy-MM-dd") };
            }
            else
            {
                DateTime exactStart = targetDate.Add(startTime);
                DateTime exactEnd = targetDate.Add(endTime);

                newEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = exactStart };
                newEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = exactEnd };
            }
            await CalendarSvc.Events.Insert(newEvent, "primary").ExecuteAsync();
        }

        public async Task CreateTaskAsync(string title, DateTime targetDate, TimeSpan startTime, bool isAllDay)
        {
            var newTask = new Google.Apis.Tasks.v1.Data.Task { Title = title };

            newTask.Due = targetDate.ToString("yyyy-MM-dd'T'00:00:00.000'Z'");

            await TasksSvc.Tasks.Insert(newTask, "@default").ExecuteAsync();
        }

        public async Task DeleteItemAsync(string itemId, bool isEvent)
        {
            if (isEvent)
            {
                if (CalendarSvc != null)
                {
                    await CalendarSvc.Events.Delete("primary", itemId).ExecuteAsync();
                }
            }
            else
            {
                if (TasksSvc != null)
                {
                    await TasksSvc.Tasks.Delete("@default", itemId).ExecuteAsync();
                }
            }
        }
    }
}
