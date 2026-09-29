using Microsoft.Win32;
using System;

namespace TodoApp.Services
{
    public static class StartupService
    {
        private const string RegistryKeyName = "PersonalTodoAssistant";

        public static bool IsStartupEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key == null) return false;
                    var value = key.GetValue(RegistryKeyName);
                    return value != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetStartup(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;

                    if (enable)
                    {
                        // Get path of current executable
                        string? exePath = Environment.ProcessPath;
                        if (string.IsNullOrEmpty(exePath))
                        {
                            exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                            // Under .NET Core/8.0, Assembly.Location might be dll. Check if exe exists.
                            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            {
                                exePath = System.IO.Path.ChangeExtension(exePath, ".exe");
                            }
                        }

                        // Run with --startup parameter so the app knows it was started on boot and should minimize to tray
                        string commandValue = $"\"{exePath}\" --startup";
                        key.SetValue(RegistryKeyName, commandValue);
                    }
                    else
                    {
                        key.DeleteValue(RegistryKeyName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting startup: {ex.Message}");
            }
        }
    }
}
