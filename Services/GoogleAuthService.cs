using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    public class GoogleAuthService
    {
        // 暴露出两个服务，供 FlyoutWindow 调用
        public CalendarService? CalendarSvc { get; private set; }
        public TasksService? TasksSvc { get; private set; }

        public async Task AuthorizeAsync()
        {
            // 请求的权限范围：日历（只读以保护隐私） + 任务（读写以支持勾选完成）
            string[] scopes = {
                CalendarService.Scope.CalendarReadonly,
                TasksService.Scope.Tasks
            };

            // 登录状态的保存路径（存在应用的独立存储区，避免每次打开都重新登录）
            string tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout", "GoogleToken");

            UserCredential credential;

            // 💡 这里会读取你的 credentials.json
            try
            {
                // 💡 核心修复：获取程序实际运行所在的绝对路径，然后再拼接文件名
                string credPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");

                using (var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read))
                {
                    // 唤起系统默认浏览器进行 Google 授权
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(tokenPath, true));
                }
            }
            catch (FileNotFoundException)
            {
                // 为了方便排错，我们可以把程序实际去寻找的路径打印出来
                string wrongPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");
                throw new Exception($"找不到秘钥文件！\n程序去这里找了，但是没找到：\n{wrongPath}\n请检查文件是否设置为“内容”并“如果较新则复制”。");
            }

            // 授权成功后，初始化日历服务
            CalendarSvc = new CalendarService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Task Flyout",
            });

            // 初始化任务服务
            TasksSvc = new TasksService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Task Flyout",
            });
        }
    }
}