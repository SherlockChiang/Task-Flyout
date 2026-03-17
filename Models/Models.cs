namespace Task_Flyout
{
    // 日程的 UI 绑定模型
    public class EventViewModel
    {
        public string Summary { get; set; }
        public string DisplayTime { get; set; }
    }

    // 任务的 UI 绑定模型
    public class TaskViewModel
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public bool IsCompleted { get; set; }
    }
}