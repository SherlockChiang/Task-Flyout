using System;
using Microsoft.UI.Xaml.Media;

namespace Task_Flyout.Services
{
    internal static class NextRenderHelper
    {
        public static void RunOnce(Action action)
        {
            EventHandler<object>? handler = null;
            handler = (_, _) =>
            {
                CompositionTarget.Rendering -= handler;
                action();
            };
            CompositionTarget.Rendering += handler;
        }
    }
}
