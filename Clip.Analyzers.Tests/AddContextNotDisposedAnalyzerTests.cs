using Verify = Clip.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Clip.Analyzers.AddContextNotDisposedAnalyzer>;

namespace Clip.Analyzers.Tests;

public class AddContextNotDisposedAnalyzerTests
{
    [Fact]
    public async Task UsingStatement_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    using (logger.AddContext(new { RequestId = "abc" })) { }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsingDeclaration_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    using var scope = logger.AddContext(new { RequestId = "abc" });
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AssignedToVariable_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    var scope = logger.AddContext(new { RequestId = "abc" });
                                    scope.Dispose();
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Discarded_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    {|#0:logger.AddContext(new { RequestId = "abc" })|};
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.AddContextReturnDiscarded).WithLocation(0));
    }
}
