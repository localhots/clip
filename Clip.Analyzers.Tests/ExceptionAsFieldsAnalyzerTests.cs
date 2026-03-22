using Verify = Clip.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Clip.Analyzers.ExceptionAsFieldsAnalyzer>;

namespace Clip.Analyzers.Tests;

public class ExceptionAsFieldsAnalyzerTests
{
    [Fact]
    public async Task ExceptionInAnonymousType_Diagnostic()
    {
        const string test = """
                            using System;
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    try { }
                                    catch (Exception ex) {
                                        logger.Info("failed", new { {|#0:Error = ex|} });
                                    }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.ExceptionAsFields)
                .WithLocation(0)
                .WithArguments("Error"));
    }

    [Fact]
    public async Task DerivedExceptionInAnonymousType_Diagnostic()
    {
        const string test = """
                            using System;
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    try { }
                                    catch (InvalidOperationException ex) {
                                        logger.Warning("failed", new { {|#0:Ex = ex|} });
                                    }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.ExceptionAsFields)
                .WithLocation(0)
                .WithArguments("Ex"));
    }

    [Fact]
    public async Task ErrorWithExceptionOverload_NoDiagnostic()
    {
        const string test = """
                            using System;
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    try { }
                                    catch (Exception ex) {
                                        logger.Error("failed", ex);
                                    }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonExceptionFields_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("ok", new { Code = 200 });
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MixedFieldsWithException_DiagnosticOnExceptionOnly()
    {
        const string test = """
                            using System;
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    try { }
                                    catch (Exception ex) {
                                        logger.Info("failed", new { Code = 500, {|#0:Error = ex|} });
                                    }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.ExceptionAsFields)
                .WithLocation(0)
                .WithArguments("Error"));
    }
}
