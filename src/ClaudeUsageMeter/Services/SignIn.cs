using System.Diagnostics;
using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Services;

/// <summary>
/// Opens a terminal running the official <c>claude auth login</c> so the user signs in themselves.
/// The meter never sees or touches the resulting credentials.
/// </summary>
public static class SignIn
{
    public static bool Launch(string? cliPath)
    {
        var cli = CliLocator.Locate(cliPath);
        if (cli is null) return false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                // cmd /k keeps the window open so the user can read the result.
                Arguments = $"/k \"title Claude sign-in && \"{cli}\" auth login\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            ChildEnvironment.Apply(psi.Environment);
            // Keep normal output for an interactive login.
            psi.Environment.Remove("NO_COLOR");
            psi.Environment.Remove("FORCE_COLOR");
            psi.Environment.Remove("DISABLE_TELEMETRY");
            psi.Environment.Remove("DISABLE_ERROR_REPORTING");
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
