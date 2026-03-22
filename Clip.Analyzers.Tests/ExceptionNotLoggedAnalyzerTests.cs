using Verify = Clip.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Clip.Analyzers.ExceptionNotLoggedAnalyzer>;

namespace Clip.Analyzers.Tests;

public class ExceptionNotLoggedAnalyzerTests
{
    [Fact]
    public async Task ErrorWithException_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            using System;
                            class C {
                                void M(ILogger logger) {
                                    try { throw new Exception(); }
                                    catch (Exception ex) {
                                        logger.Error("failed", ex, new { Op = "save" });
                                    }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ErrorOutsideCatch_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Error("something went wrong");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ErrorWithoutExceptionInCatch_Diagnostic()
    {
        const string test = """
                            using Clip;
                            using System;
                            class C {
                                void M(ILogger logger) {
                                    try { throw new Exception(); }
                                    catch (Exception ex) {
                                        {|#0:logger.Error("failed", new { Op = "save" })|};
                                    }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.ExceptionNotLogged).WithLocation(0));
    }

    [Fact]
    public async Task ErrorNoFieldsInCatch_Diagnostic()
    {
        const string test = """
                            using Clip;
                            using System;
                            class C {
                                void M(ILogger logger) {
                                    try { throw new Exception(); }
                                    catch {
                                        {|#0:logger.Error("failed")|};
                                    }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.ExceptionNotLogged).WithLocation(0));
    }

    [Fact]
    public async Task InfoInCatch_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            using System;
                            class C {
                                void M(ILogger logger) {
                                    try { throw new Exception(); }
                                    catch (Exception ex) {
                                        logger.Info("retrying");
                                    }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }
}
