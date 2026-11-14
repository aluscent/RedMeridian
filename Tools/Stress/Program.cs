using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using RedMeridian.Domain;

const string usage = "Options: --regions N --orbits N --ships N --routes N --batches N --speed 1|2|4";
if (args is ["--help"])
{
    Console.WriteLine(usage);
    return 0;
}

try
{
    var options = Parse(args);
    int regions = options["--regions"], orbits = options["--orbits"], ships = options["--ships"];
    int routes = options["--routes"], batches = options["--batches"], speed = options["--speed"];
    if (regions < 1 || batches < 1 || speed is not (1 or 2 or 4) || routes > ships ||
        (routes > 0 && (long)regions + orbits < 2))
        throw new ArgumentException("Use at least one region and batch, a supported speed, and no more routes than ships. Routes need two bases.");

    // The current engine keeps three byte grids and one int grid for every base.
    long gridBytes = ((long)regions + orbits) * Colony.Size * Colony.Size * 7;
    long memoryBudget = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    if (gridBytes > memoryBudget / 2)
        throw new ArgumentException($"This workload needs at least {MiB(gridBytes):N0} MiB for colony grids alone. " +
            $"The runtime reports {MiB(memoryBudget):N0} MiB available. Reduce the base count for the current engine.");

    Console.WriteLine($"{RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, " +
        $"{Environment.ProcessorCount} logical CPUs, {RuntimeInformation.FrameworkDescription}");
    Console.WriteLine($"{regions:N0} regions, {orbits:N0} orbital bases, {ships:N0} spacecraft, {routes:N0} routes, " +
        $"{batches:N0} batches at {speed}×. Estimated colony grid memory: {MiB(gridBytes):N0} MiB.");
    var rules = new GameRules(Path.Combine(AppContext.BaseDirectory,"Config"));
    var scenario = new StressScenario(rules,regions,orbits,ships,routes);
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var save = Run(scenario,batches,speed,json,out long finalTick);

    GC.Collect(); // Release the first campaign before measuring a load.
    var clock = Stopwatch.StartNew();
    var restored = scenario.Restore(JsonSerializer.Deserialize<SaveData>(save,json)!);
    Console.WriteLine($"Save load and restore: {clock.Elapsed.TotalMilliseconds:N1} ms");
    var restoredSave = restored.Export();
    if (restoredSave.Tick != finalTick || restoredSave.Space?.Bases.Count != regions + orbits - 1 ||
        restoredSave.Space.Ships.Length != ships || restoredSave.Space.Routes?.Length != routes)
        throw new InvalidOperationException("Save round trip changed the workload counts or simulation clock.");
    using var process = Process.GetCurrentProcess();
    Console.WriteLine($"Peak process working set: {MiB(process.PeakWorkingSet64):N1} MiB");
    return 0;
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine(usage);
    return 2;
}

static Dictionary<string,int> Parse(string[] arguments)
{
    var result = new Dictionary<string,int>
    {
        ["--regions"] = 5, ["--orbits"] = 2, ["--ships"] = 64,
        ["--routes"] = 32, ["--batches"] = 5, ["--speed"] = 1
    };
    for (int i = 0; i < arguments.Length; i += 2)
    {
        if (i + 1 == arguments.Length || !result.ContainsKey(arguments[i]) ||
            !int.TryParse(arguments[i + 1],out int value) || value < 0)
            throw new ArgumentException($"Expected a nonnegative integer for a known option near '{arguments[i]}'.");
        result[arguments[i]] = value;
    }
    return result;
}

static byte[] Run(StressScenario scenario, int batches, int speed, JsonSerializerOptions json, out long finalTick)
{
    var clock = Stopwatch.StartNew();
    var program = scenario.Restore(scenario.InitialSave);
    Console.WriteLine($"Campaign setup: {clock.Elapsed.TotalMilliseconds:N1} ms");
    Console.WriteLine($"Managed heap after setup: {MiB(GC.GetTotalMemory(true)):N1} MiB");
    // Match the current session clock: one game tick per two real seconds at 1×.
    // A batch measures existing ticks back-to-back, without an idle wait or UI.
    const int batchSeconds = 30;
    int ticksPerBatch = batchSeconds / 2 * speed;
    var times = new double[batches];
    int peakOverdue = 0;
    for (int batch = 0; batch < batches; batch++)
    {
        clock.Restart();
        for (int tick = 0; tick < ticksPerBatch; tick++) program.Step();
        times[batch] = clock.Elapsed.TotalMilliseconds;
        peakOverdue = Math.Max(peakOverdue,program.CountDueShipments());
    }
    Array.Sort(times);
    Console.WriteLine($"Batch elapsed time: median {times[times.Length / 2]:N2} ms, " +
        $"p95 {times[(int)Math.Ceiling(times.Length * 0.95) - 1]:N2} ms, max {times[^1]:N2} ms");
    Console.WriteLine($"Due cargo still in transit at batch boundaries: peak {peakOverdue:N0}, final {program.CountDueShipments():N0}");
    finalTick = program.Mars.Tick;
    clock.Restart();
    byte[] save = JsonSerializer.SerializeToUtf8Bytes(program.Export(),json);
    Console.WriteLine($"Save export and encoding: {clock.Elapsed.TotalMilliseconds:N1} ms, {save.Length:N0} bytes");
    return save;
}

static double MiB(long bytes) => bytes / 1048576.0;
