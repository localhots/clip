using Verify = Clip.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<
    Clip.Analyzers.AddContextNotDisposedAnalyzer,
    Clip.Analyzers.AddContextNotDisposedCodeFix>;

namespace Clip.Analyzers.Tests;

public class AddContextNotDisposedCodeFixTests
{
    [Fact]
    public async Task Discarded_AddsUsing()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    {|#0:logger.AddContext(new { RequestId = "abc" })|};
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   using var _ = logger.AddContext(new { RequestId = "abc" });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.AddContextReturnDiscarded).WithLocation(0));
    }

    [Fact]
    public async Task StaticCall_Discarded_AddsUsing()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M() {
                                    {|#0:Logger.AddContext(new { RequestId = "abc" })|};
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M() {
                                   using var _ = Logger.AddContext(new { RequestId = "abc" });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.AddContextReturnDiscarded).WithLocation(0));
    }
}
