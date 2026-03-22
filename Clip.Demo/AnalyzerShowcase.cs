// Analyzer showcase — open this file in Rider to see all Clip diagnostics.
// Every method below triggers exactly one analyzer. Do not "fix" these;
// they exist so you can verify the analyzers work.

//
// NOTE: Comment the pragma to see the analyzer in action.
//

#pragma warning disable CS8321, CS0168, CLIP001, CLIP002, CLIP003, CLIP004, CLIP005, CLIP006, CLIP007, CLIP008

using Clip;

internal static class AnalyzerShowcase
{
    private static void Showcase(Logger logger)
    {
        // CLIP001 (error) — Invalid fields argument
        // The fields parameter must be an anonymous object or dictionary, not a primitive.
        logger.Info("User count", 42);

        // CLIP002 (warning) — Message contains template syntax
        // Clip uses fields, not message templates. {UserId} won't be interpolated.
        logger.Info("User {UserId} logged in", 42);

        // CLIP003 (warning) — AddContext return value discarded
        // The returned scope must be disposed or context fields leak forever.
        Logger.AddContext(new { TraceId = "abc" });

        // CLIP004 (info) — Exception not passed to Error
        // The caught exception should be forwarded so it appears in the log entry.
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            logger.Error("Operation failed");
        }

        // CLIP005 (warning) — Unreachable code after Fatal.
        // Fatal calls Environment.Exit; nothing after it will run.
        logger.Fatal("Critical failure");
        logger.Info("This is unreachable");

        // CLIP006 (warning) — Interpolated string in log message.
        // Interpolated strings bake data into the message, losing structured fields.
        var user = "alice";
        logger.Info($"User {user} logged in");

        // CLIP007 (info) — Exception passed as fields.
        // The exception should use the Error(message, exception) overload.
        try
        {
            throw new InvalidOperationException();
        }
        catch (Exception ex2)
        {
            logger.Info("Operation failed", ex2);
        }

        // CLIP008 (info) — Empty log message.
        // Messages should be descriptive plain-text strings.
        logger.Info("");

        // CLIP008 (info) — Empty log message.
        // Messages should be descriptive plain-text strings.
        logger.Warning("operation failed");
    }
}
