using System.Globalization;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
using var ui = new WorkspaceSmokeSession();
PioneerEventLoggerRuntimeSmoke.RunWithoutPioneer(ui);
PioneerEventLoggerRuntimeSmoke.Run(ui);
Console.WriteLine("PASS Pioneer EventLogger packet integration");
