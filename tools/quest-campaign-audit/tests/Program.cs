if (args.Length == 3 && args[0] == "stage-odin-host-proof")
{
    HostProofStage.Stage(args[1], args[2]);
    return;
}
int assertions = 0;
StandaloneOdinTests.Run(Path.GetFullPath(args[0]), (condition, message) => {
    assertions++; if (!condition) throw new InvalidOperationException(message);
});
Console.WriteLine($"Original Odin AOT selector and negative drift/fallback controls: {assertions} assertions passed.");
