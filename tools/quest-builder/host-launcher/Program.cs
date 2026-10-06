using System.Diagnostics;
using System.Globalization;

// Native apphost: Unity CreateProcess cannot launch Windows .cmd/.py wrappers.
// Preserve the native Bee protocol, including its stdin canary and exit status.
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            string? backend = Environment.GetEnvironmentVariable("GHVRQ_BEE_REAL_PATH");
            string? value = Environment.GetEnvironmentVariable("GHVRQ_BEE_THREADS");
            if (string.IsNullOrWhiteSpace(backend) || !Path.IsPathFullyQualified(backend) || !File.Exists(backend)
                || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int jobs) || jobs < 1 || jobs > 1024)
                throw new InvalidOperationException("Verified Bee executable and bounded threads are required.");
            var start = new ProcessStartInfo(backend) { UseShellExecute = false, RedirectStandardInput = true };
            // The actual 2021.3 backend stops option parsing at the first target.
            // Prepend the cap; appending it after target silently leaves default
            // concurrency active. The witnessed driver has no thread argument.
            if (args.Any(argument => argument == "-j" || argument == "--threads" || argument.StartsWith("--threads=", StringComparison.Ordinal)))
                throw new InvalidOperationException("Unexpected existing Bee thread override.");
            start.ArgumentList.Add("--threads=" + jobs.ToString(CultureInfo.InvariantCulture));
            foreach (string argument in args)
                start.ArgumentList.Add(argument);
            using Process child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start native Bee backend.");
            // Do not await the pump after child exit: the Editor may keep its pipe
            // open until this wrapper returns. EOF before exit reaches the canary.
            _ = ForwardInput(child);
            await child.WaitForExitAsync();
            return child.ExitCode;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Quest build scheduling: " + error.GetType().Name + ": " + error.Message);
            return 126;
        }
    }

    private static async Task ForwardInput(Process child)
    {
        try { await Console.OpenStandardInput().CopyToAsync(child.StandardInput.BaseStream); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            try { child.StandardInput.Close(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
