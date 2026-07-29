using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace PCSteward.ScheduledHost;

internal static class WindowsScheduledCheckNotification
{
    public static void TryShow(string title, string body)
    {
        try
        {
            XmlDocument document = new();
            document.LoadXml($"<toast><visual><binding template=\"ToastGeneric\"><text>{Escape(title)}</text><text>{Escape(body)}</text></binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier("PCSteward").Show(new ToastNotification(document));
        }
        catch (Exception) when (OperatingSystem.IsWindows()) { }
    }

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
