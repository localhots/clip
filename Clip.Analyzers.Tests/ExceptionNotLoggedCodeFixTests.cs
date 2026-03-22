using Verify = Clip.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<
    Clip.Analyzers.ExceptionNotLoggedAnalyzer,
    Clip.Analyzers.ExceptionNotLoggedCodeFix>;

namespace Clip.Analyzers.Tests;

public class ExceptionNotLoggedCodeFixTests
{
    [Fact]
    public async Task NamedCatchVariable_InsertsException()
    {
        const string test = """
                            using System;
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    try { }
                                    catch (Exception ex) {
                                        {|#0:logger.Error("Failed")|};
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
                                       logger.Error("Failed", ex);
                                   }
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.ExceptionNotLogged).WithLocation(0));
    }

    [Fact]
    public async Task UnnamedCatchVariable_AddsName()
    {
        const string test = """
                            using System;
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    try { }
                                    catch (Exception) {
                                        {|#0:logger.Error("Failed")|};
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
                                       logger.Error("Failed", ex);
                                   }
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.ExceptionNotLogged).WithLocation(0));
    }

    [Fact]
    public async Task ErrorWithFields_InsertsExceptionBeforeFields()
    {
        const string test = """
                            using System;
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    try { }
                                    catch (Exception ex) {
                                        {|#0:logger.Error("Failed", new { Code = 500 })|};
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
                                       logger.Error("Failed", ex, new { Code = 500 });
                                   }
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.ExceptionNotLogged).WithLocation(0));
    }
}
