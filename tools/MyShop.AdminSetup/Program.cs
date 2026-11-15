using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Identity;

return await AdminSetup.RunAsync(args);

internal static class AdminSetup
{
    private const string SecretsId = "MyShop-Development";

    public static async Task<int> RunAsync(string[] args)
    {
        string? protectedFile = null;
        var secretsUpdated = false;
        try
        {
            if (args.Any(argument => argument is "--help" or "-h"))
            {
                Console.WriteLine("MyShop local administrator setup");
                Console.WriteLine("dotnet run --project tools/MyShop.AdminSetup -- [--generate] [--username NAME] [--project PATH]");
                Console.WriteLine("Default: prompts for a username and a masked password of 12–256 characters, then confirms it.");
                Console.WriteLine("--generate: creates a random password for admin (or --username), saved only in a protected local access file.");
                Console.WriteLine("--project: path to eShop.Web.csproj, or its directory; otherwise searches current/parent directories.");
                Console.WriteLine("Invoking setup configures or rotates the Development account. No credentials are written into the repository.");
                return 0;
            }

            var (generate, usernameArgument, projectArgument) = ParseArguments(args);
            var project = FindHostProject(projectArgument);
            var username = generate
                ? usernameArgument ?? "admin"
                : usernameArgument ?? ReadUsername();
            if (string.IsNullOrWhiteSpace(username) || username.Length > 100 || username.Any(char.IsControl))
                throw new ArgumentException("The username must contain 1–100 printable characters.");

            string password;
            if (generate)
            {
                password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                protectedFile = WriteProtectedAccessFile(username, password);
            }
            else
            {
                password = ReadPassword("New password: ");
                if (password.Length < 12 || password.Length > 256)
                    throw new ArgumentException("Use a password between 12 and 256 characters.");
                var confirmation = ReadPassword("Confirm password: ");
                if (!string.Equals(password, confirmation, StringComparison.Ordinal))
                    throw new ArgumentException("The passwords did not match. Nothing was changed.");
            }

            var hash = new PasswordHasher<object>().HashPassword(new object(), password);
            var values = new Dictionary<string, string>
            {
                ["AdminAuthentication:UserName"] = username,
                ["AdminAuthentication:PasswordHash"] = hash
            };
            await StoreSecretsAsync(project, values);
            secretsUpdated = true;

            var accessPath = GetAccessPath();
            if (generate)
            {
                File.Move(protectedFile!, accessPath, overwrite: true);
                protectedFile = null;
                Console.WriteLine("Administrator configured. Read your generated credentials in:");
                Console.WriteLine(accessPath);
            }
            else
            {
                // A previous generated password is now invalid; don't leave misleading access notes.
                if (File.Exists(accessPath)) File.Delete(accessPath);
                Console.WriteLine("Administrator configured with your chosen password.");
            }
            Console.WriteLine("Username: " + username);
            Console.WriteLine("Sign in at /login while the app runs in Development. Open sessions revalidate within one minute.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Setup canceled. No credentials were changed.");
            return 1;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException
            or System.Xml.XmlException)
        {
            Console.Error.WriteLine(exception.Message);
            if (secretsUpdated)
            {
                Console.Error.WriteLine("The administrator configuration was saved, but the local access-file update failed.");
                if (protectedFile != null) Console.Error.WriteLine("Generated credentials remain protected in: " + protectedFile);
            }
            return 1;
        }
        finally
        {
            if (!secretsUpdated && protectedFile != null && File.Exists(protectedFile))
                File.Delete(protectedFile);
        }
    }

    private static (bool Generate, string? UserName, string? Project) ParseArguments(string[] args)
    {
        var generate = false;
        string? username = null;
        string? project = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--generate": generate = true; break;
                case "--username" when index + 1 < args.Length: username = args[++index].Trim(); break;
                case "--project" when index + 1 < args.Length: project = args[++index]; break;
                default: throw new ArgumentException("Unknown or incomplete option: " + args[index] + ". Use --help for usage.");
            }
        }
        return (generate, username, project);
    }

    private static string FindHostProject(string? argument)
    {
        if (argument != null)
        {
            var specified = Path.GetFullPath(argument);
            if (Directory.Exists(specified)) specified = Path.Combine(specified, "eShop.Web.csproj");
            return ValidateProject(specified);
        }

        var location = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (location != null)
        {
            var candidate = Path.Combine(location.FullName, "eShop.Web", "eShop.Web.csproj");
            if (File.Exists(candidate)) return ValidateProject(candidate);
            candidate = Path.Combine(location.FullName, "eShop.Web.csproj");
            if (File.Exists(candidate)) return ValidateProject(candidate);
            location = location.Parent;
        }
        throw new ArgumentException("Could not locate eShop.Web.csproj. Run from this repository or provide --project PATH.");
    }

    private static string ValidateProject(string path)
    {
        if (!File.Exists(path)) throw new ArgumentException("The host project was not found: " + path);
        var project = XDocument.Load(path);
        if (!project.Descendants("UserSecretsId").Any(element => element.Value == SecretsId))
            throw new ArgumentException("The selected project does not use the MyShop-Development secret store.");
        return path;
    }

    private static string ReadUsername()
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Interactive setup requires a terminal. Use --generate for a protected generated password.");
        Console.Write("Username [admin]: ");
        var input = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(input) ? "admin" : input;
    }

    private static string ReadPassword(string prompt)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("A password must be entered interactively, not in command-line arguments or redirected input.");
        Console.Write(prompt);
        var value = new StringBuilder();
        var previousControlMode = Console.TreatControlCAsInput;
        try
        {
            Console.TreatControlCAsInput = true;
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    throw new OperationCanceledException();
                if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
                if (key.Key == ConsoleKey.Backspace && value.Length > 0)
                { value.Length--; Console.Write("\b \b"); }
                else if (!char.IsControl(key.KeyChar))
                {
                    if (value.Length >= 256) throw new ArgumentException("A password cannot exceed 256 characters.");
                    value.Append(key.KeyChar);
                    Console.Write('*');
                }
            }
        }
        finally { Console.TreatControlCAsInput = previousControlMode; }
    }

    private static string GetAccessPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyShop", "local-admin.txt");

    private static string WriteProtectedAccessFile(string username, string password)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Generated access-file setup is supported on Windows. Use interactive setup on this platform.");
        var directory = Path.GetDirectoryName(GetAccessPath())!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, "local-admin-" + Guid.NewGuid().ToString("N") + ".tmp");
        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Could not determine the current Windows user.");
        var security = new FileSecurity();
        security.SetOwner(identity);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
        using var stream = new FileInfo(tempPath).Create(FileMode.CreateNew, FileSystemRights.FullControl,
            FileShare.None, 4096, FileOptions.None, security);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine("MyShop local Development administrator");
        writer.WriteLine("Username: " + username);
        writer.WriteLine("Password: " + password);
        writer.WriteLine("Sign-in route: /login");
        writer.WriteLine("Created: " + DateTimeOffset.Now.ToString("O"));
        return tempPath;
    }

    private static async Task StoreSecretsAsync(string project, Dictionary<string, string> values)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "user-secrets", "set", "--project", project }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch dotnet user-secrets.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(values));
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("dotnet user-secrets could not save the account. Check that a .NET 10 SDK is installed and the secret store is writable.");
    }
}
