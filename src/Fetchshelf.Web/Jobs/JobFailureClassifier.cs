using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Web.Jobs;

/// <summary>Tells the registry which failures are MAM refusing the session or the account, so the Errors page can group them.</summary>
public static class JobFailureClassifier
{
    public static JobFailureKind Classify(Exception exception) => exception switch
    {
        MamForbiddenException => JobFailureKind.MamRefused,
        MamAccountBlockedException => JobFailureKind.MamBlocked,
        MamRateLimitedException => JobFailureKind.MamRateLimited,
        _ => JobFailureKind.Other,
    };
}
