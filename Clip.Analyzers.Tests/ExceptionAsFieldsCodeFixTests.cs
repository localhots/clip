using Verify = Clip.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<
    Clip.Analyzers.ExceptionAsFieldsAnalyzer,
    Clip.Analyzers.ExceptionAsFieldsCodeFix>;

namespace Clip.Analyzers.Tests;

public class ExceptionAsFieldsCodeFixTests
{
    [Fact]
    public async Task ExceptionOnly_ChangesToError()
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
        const string fix = """
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
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.ExceptionAsFields)
                .WithLocation(0)
                .WithArguments("Error"));
    }

    [Fact]
    public async Task ExceptionWithOtherFields_KeepsRemainingFields()
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
        const string fix = """
                           using System;
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   try { }
                                   catch (Exception ex) {
                                       logger.Error("failed", ex, new { Code = 500 });
                                   }
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.ExceptionAsFields)
                .WithLocation(0)
                .WithArguments("Error"));
    }

    [Fact]
    public async Task WarningWithException_ChangesToError()
    {
        const string test = """
                            using System;
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    try { }
                                    catch (Exception ex) {
                                        logger.Warning("failed", new { {|#0:Ex = ex|} });
                                    }
                                }
                            }
                            """;
        const string fix = """
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
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.ExceptionAsFields)
                .WithLocation(0)
                .WithArguments("Ex"));
    }
}
