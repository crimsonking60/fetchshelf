using Fetchshelf.Data.MamCalls;

namespace Fetchshelf.Web.Jobs;

/// <summary>Runs <see cref="MamCallRecorder"/>'s background loop, which writes the MAM calls handed to it.</summary>
public sealed class MamCallRecording(MamCallRecorder recorder) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => recorder.RunAsync(stoppingToken);
}
