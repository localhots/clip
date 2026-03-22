using Verify = Clip.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Clip.Analyzers.InterpolatedStringMessageAnalyzer>;

namespace Clip.Analyzers.Tests;

public class InterpolatedStringMessageAnalyzerTests
{
    [Fact]
    public async Task PlainString_NoDiagnostic()
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
    public async Task ConstString_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    const string msg = "User logged in";
                                    logger.Info(msg);
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InterpolatedString_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger, string name) {
                                    logger.Info({|#0:$"User {name} logged in"|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InterpolatedStringMessage).WithLocation(0));
    }

    [Fact]
    public async Task InterpolatedStringWithMultiple_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger, string name, int age) {
                                    logger.Info({|#0:$"User {name} age {age}"|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InterpolatedStringMessage).WithLocation(0));
    }

    [Fact]
    public async Task InterpolatedStringNoInterpolations_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:$"User logged in"|});
                                }
                            }
                            """;
        // $"literal" with no interpolations is still an interpolated string — flag it
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InterpolatedStringMessage).WithLocation(0));
    }
}
