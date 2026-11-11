using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Notifications;

namespace Fetchshelf.Core.Tests;

/// <summary>What job runs, MAM answers and the Download job mean for notifications.</summary>
public class NotificationRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 8, 30, 0, TimeSpan.Zero);

    private static JobRunRecord Run(JobRunResult result, string? summary = null, int hours = 0, JobFailureKind? kind = null) =>
        new(T0.AddHours(hours), result, summary, T0.AddHours(hours).AddMinutes(1), FailureKind: kind);

    private static JobRunFinished Finished(JobRunRecord run, JobRunRecord? previous, JobFailure? before, JobFailure? after) =>
        new("Link", run, previous, before, after);

    private static JobFailure Failure(string error = "qBittorrent down", int runs = 1, int hours = 0, JobFailureKind kind = JobFailureKind.Other) =>
        new(T0.AddHours(hours), runs, error, kind);

    [Fact]
    public void FailingAfterSuccess_IsJobFailingWithTheError()
    {
        var run = Run(JobRunResult.Failure, "the connection to qBittorrent failed");

        var notification = JobNotificationRules.From(Finished(run, Run(JobRunResult.Success, hours: -1), null, Failure("the connection to qBittorrent failed")))!;

        Assert.Equal(NotificationEvent.JobFailing, notification.Event);
        Assert.Equal("Job Link failed: the connection to qBittorrent failed", notification.Message);
        Assert.Equal("/Jobs/History?job=Link", notification.LinkPath);
        Assert.Null(notification.SupersededBy);
    }

    [Fact]
    public void FailingAfterFailing_SendsNothing() =>
        Assert.Null(JobNotificationRules.From(Finished(Run(JobRunResult.Failure, "x"), Run(JobRunResult.Failure, "x", -1), Failure(runs: 1), Failure(runs: 2))));

    [Fact]
    public void FirstRunEverFailing_IsJobFailing() =>
        Assert.Equal(
            NotificationEvent.JobFailing,
            JobNotificationRules.From(Finished(Run(JobRunResult.Failure, "boom"), null, null, Failure("boom")))!.Event);

    [Fact]
    public void SuccessAfterFailure_IsRecoveredWithTheCountAndSinceWhen()
    {
        var before = Failure(runs: 6, hours: -3);

        var notification = JobNotificationRules.From(Finished(Run(JobRunResult.Success), Run(JobRunResult.Failure, "x", -1), before, null))!;

        Assert.Equal(NotificationEvent.JobRecovered, notification.Event);
        Assert.Equal("Job Link works again, after 6 failed runs since 05:30 UTC.", notification.Message);
    }

    [Fact]
    public void RecoveredAfterAFailureOnAnEarlierDay_SaysTheDate() =>
        Assert.Equal(
            "Job Link works again, after 1 failed run since 2026-11-10 08:30 UTC.",
            JobNotificationRules.From(Finished(Run(JobRunResult.Success), null, Failure(runs: 1, hours: -24), null))!.Message);

    [Fact]
    public void FinishedWithErrorsAfterFailure_IsRecovered() =>
        Assert.Equal(
            NotificationEvent.JobRecovered,
            JobNotificationRules.From(Finished(Run(JobRunResult.CompletedWithErrors, "1 could not be linked"), null, Failure(), null))!.Event);

    [Fact]
    public void CancelledAfterFailure_SendsNothing() =>
        Assert.Null(JobNotificationRules.From(Finished(Run(JobRunResult.Cancelled), Run(JobRunResult.Failure, "x", -1), Failure(), null)));

    [Fact]
    public void ErrorsAppearing_AreJobErrorsAndErrorsAgainAreNot()
    {
        const string summary = "3 torrents linked; 2 could not be linked; see the Errors page";
        var errors = Run(JobRunResult.CompletedWithErrors, summary);

        var first = JobNotificationRules.From(Finished(errors, Run(JobRunResult.Success, hours: -1), null, null))!;
        var again = JobNotificationRules.From(Finished(errors, Run(JobRunResult.CompletedWithErrors, summary, -1), null, null));

        Assert.Equal((NotificationEvent.JobErrors, $"Link: {summary}", "/Errors"), (first.Event, first.Message, first.LinkPath));
        Assert.Null(again);
    }

    [Fact]
    public void SuccessAfterSuccess_SendsNothing() =>
        Assert.Null(JobNotificationRules.From(Finished(Run(JobRunResult.Success), Run(JobRunResult.Success, hours: -1), null, null)));

    [Fact]
    public void AJobFailingBeforeARestartThatFailsAgain_SendsNothing() =>
        // The registry restores the failing state at startup, so the run after the restart starts from "already failing".
        Assert.Null(JobNotificationRules.From(Finished(Run(JobRunResult.Failure, "x"), Run(JobRunResult.Failure, "x", -1), Failure(runs: 3), Failure(runs: 4))));

    [Theory]
    [InlineData(JobFailureKind.MamRefused, NotificationEvent.MamRefused)]
    [InlineData(JobFailureKind.MamBlocked, NotificationEvent.MamRefused)]
    [InlineData(JobFailureKind.MamRateLimited, NotificationEvent.RateLimited)]
    public void AFailureBecauseOfMam_IsSupersededByTheMamEventSoOneRefusalIsNotOneMessagePerJob(JobFailureKind kind, NotificationEvent superseding)
    {
        var failing = JobNotificationRules.From(Finished(Run(JobRunResult.Failure, "403", kind: kind), null, null, Failure("403", kind: kind)))!;
        var recovered = JobNotificationRules.From(Finished(Run(JobRunResult.Success), null, Failure("403", kind: kind), null))!;

        Assert.Equal(superseding, failing.SupersededBy);
        Assert.Equal(superseding, recovered.SupersededBy);
    }

    [Fact]
    public void JobRunNotifier_PublishesWhatTheRulesFindAndNothingElse()
    {
        var sink = new RecordingSink();
        var listener = new JobRunNotifier(sink);

        listener.RunFinished(Finished(Run(JobRunResult.Success), null, null, null));
        listener.RunFinished(Finished(Run(JobRunResult.Failure, "boom"), null, null, Failure("boom")));

        Assert.Equal(NotificationEvent.JobFailing, Assert.Single(sink.Published).Event);
    }

    [Theory]
    [InlineData(null, 0, false, null)]
    [InlineData(null, 1, true, 1L)]
    [InlineData(0L, 1, true, 1L)]
    [InlineData(1L, 1, false, null)]
    [InlineData(2L, 1, false, 1L)]
    [InlineData(1L, 2, true, 2L)]
    public void HitAndRun_ComparesTheReadCountWithTheStoredOne(long? stored, long current, bool send, long? store) =>
        Assert.Equal((send, store), AccountNotificationRules.DecideHitAndRun(stored, current));

    [Fact]
    public void HitAndRun_Message_NamesTheCountAndSaysWhatToDo()
    {
        Assert.Equal(
            "MAM counts 1 hit-and-run (Not Seeding - H&R - Not Yet Satisfied). Seed it again to avoid a penalty.",
            AccountNotificationRules.HitAndRun(1, T0).Message);
        Assert.Contains("2 hit-and-runs", AccountNotificationRules.HitAndRun(2, T0).Message);
        Assert.Contains("Seed them again", AccountNotificationRules.HitAndRun(2, T0).Message);
    }

    [Fact]
    public void MamRefused_SaysUntilWhenTheCallsArePausedAndWhatToDo() =>
        Assert.Equal(
            "MAM refused mam_id (HTTP 403). MAM jobs are paused until 14:32 UTC. Replace mam_id and restart Fetchshelf.",
            AccountNotificationRules.MamRefused(T0, new DateTimeOffset(2026, 11, 11, 14, 32, 0, TimeSpan.Zero)).Message);

    [Fact]
    public void MamBlockedAndRateLimited_AreTheirOwnTexts()
    {
        Assert.Equal(NotificationEvent.MamRefused, AccountNotificationRules.MamBlocked(T0).Event);
        Assert.Contains("HTTP 406", AccountNotificationRules.MamBlocked(T0).Message);
        Assert.Equal(
            ("MAM asked Fetchshelf to slow down (HTTP 429); MAM calls are paused for a minute.", NotificationEvent.RateLimited),
            (AccountNotificationRules.RateLimited(T0).Message, AccountNotificationRules.RateLimited(T0).Event));
    }

    [Fact]
    public void BudgetSpent_IsSentWhenSelectionsStartWaitingNotWhileTheyKeepWaiting()
    {
        const string reason = "the ratio budget is 0 B (ratio 2.01, min_ratio 2)";

        var first = AccountNotificationRules.BudgetSpent(3, reason, previousLeftQueued: 0, T0)!;

        Assert.Equal((NotificationEvent.BudgetSpent, "3 selections wait: the ratio budget is 0 B (ratio 2.01, min_ratio 2)."), (first.Event, first.Message));
        Assert.Null(AccountNotificationRules.BudgetSpent(3, reason, previousLeftQueued: 3, T0));
        Assert.Null(AccountNotificationRules.BudgetSpent(0, null, previousLeftQueued: 0, T0));
        Assert.Equal("1 selection waits: x.", AccountNotificationRules.BudgetSpent(1, "x", 0, T0)!.Message);
    }

    [Fact]
    public void Throttle_LetsSomethingThroughOnceInAWhilePerKey()
    {
        var throttle = new NotificationThrottle();
        var hour = TimeSpan.FromHours(1);

        Assert.True(throttle.Allow("rate_limited", hour, T0));
        Assert.False(throttle.Allow("rate_limited", hour, T0.AddMinutes(59)));
        Assert.True(throttle.Allow("mam_refused", hour, T0.AddMinutes(1))); // another key
        Assert.True(throttle.Allow("rate_limited", hour, T0.AddMinutes(60)));
    }

    [Fact]
    public void EventNames_RoundTripAndAllIsNotAnEvent()
    {
        foreach (var notificationEvent in NotificationEvents.All)
        {
            Assert.True(NotificationEvents.TryParse(NotificationEvents.Name(notificationEvent), out var parsed));
            Assert.Equal(notificationEvent, parsed);
        }

        Assert.Equal(10, NotificationEvents.All.Count);
        Assert.False(NotificationEvents.TryParse("all", out _));
        Assert.False(NotificationEvents.TryParse("Grabbed", out _)); // names are lower case
    }

    private sealed class RecordingSink : INotificationSink
    {
        public List<Notification> Published { get; } = [];

        public void Publish(Notification notification) => Published.Add(notification);
    }
}
