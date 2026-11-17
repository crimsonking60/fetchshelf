using Fetchshelf.Data.Account;

namespace Fetchshelf.Web.Jobs;

/// <summary>Runs <see cref="AccountSnapshotRecorder"/>'s background loop, which writes the account reads handed to it.</summary>
public sealed class AccountSnapshotRecording(AccountSnapshotRecorder recorder) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => recorder.RunAsync(stoppingToken);
}
