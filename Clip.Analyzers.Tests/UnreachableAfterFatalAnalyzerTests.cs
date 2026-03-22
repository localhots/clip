using Verify = Clip.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Clip.Analyzers.UnreachableAfterFatalAnalyzer>;

namespace Clip.Analyzers.Tests;

public class UnreachableAfterFatalAnalyzerTests
{
    [Fact]
    public async Task FatalAsLastStatement_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Fatal("shutdown");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CodeAfterFatal_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Fatal("shutdown");
                                    {|#0:var x = 42;|}
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.UnreachableAfterFatal).WithLocation(0));
    }

    [Fact]
    public async Task NonFatalLog_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Error("bad thing");
                                    var x = 42;
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FatalInIfBranch_NoDiagnosticAfterIf()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger, bool critical) {
                                    if (critical) {
                                        logger.Fatal("shutdown");
                                    }
                                    var x = 42;
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }
}
