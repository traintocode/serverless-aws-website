using System;
using System.Collections.Generic;
using System.Text;

namespace ServerlessAwsWebsite;

/// <summary>
/// Mutable state object that is passed between state machine steps in <see cref="LambdaFunctions" />
/// </summary>
public class StepFunctionsState
{
    public string? TriggeredS3Key { get; set; }
    public string? SongTitle { get; set; }
    public string? SongLyrics { get; set; }
    public string? LanguageKey { get; set; }

    /// <summary>
    /// Passed in by the EventBridge S3 trigger
    /// </summary>
    public StepFunctionsDetail? Detail { get; set; }
}

public class StepFunctionsDetail
{
    public StepFunctionsDetailObject? Object {get; set; }

}

public class StepFunctionsDetailObject
{
    public string? Key { get; set; }

}
