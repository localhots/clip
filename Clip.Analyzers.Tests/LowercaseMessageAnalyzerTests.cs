using Verify = Clip.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Clip.Analyzers.LowercaseMessageAnalyzer>;

namespace Clip.Analyzers.Tests;

public class LowercaseMessageAnalyzerTests
{
    [Fact]
    public async Task UppercaseMessage_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("User logged in");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LowercaseMessage_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:"user logged in"|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.LowercaseMessage).WithLocation(0));
    }

    [Fact]
    public async Task EmptyMessage_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonLiteralMessage_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger, string msg) {
                                    logger.Info(msg);
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NumberStartMessage_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("404 not found");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonAsciiLowercaseStart_Diagnostic()
    {
        // char.IsLower returns true for Unicode lowercase too — German "über" should be
        // flagged just like ASCII "user".
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:"über die brücke"|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.LowercaseMessage).WithLocation(0));
    }

    [Fact]
    public async Task NonAsciiUppercaseStart_NoDiagnostic()
    {
        // Already capitalized — no diagnostic.
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("Über die Brücke");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SymbolStartMessage_NoDiagnostic()
    {
        // Punctuation isn't lowercase — no diagnostic.
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("[startup] ready");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }
}
