using Verify = Clip.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<
    Clip.Analyzers.InterpolatedStringMessageAnalyzer,
    Clip.Analyzers.InterpolatedStringMessageCodeFix>;

namespace Clip.Analyzers.Tests;

public class InterpolatedStringMessageCodeFixTests
{
    [Fact]
    public async Task SingleIdentifier_ExtractsToShorthand()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger, string name) {
                                    logger.Info({|#0:$"User {name} logged in"|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger, string name) {
                                   logger.Info("User logged in", new { name });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.InterpolatedStringMessage).WithLocation(0));
    }

    [Fact]
    public async Task MemberAccess_ExtractsWithKey()
    {
        const string test = """
                            using Clip;
                            class C {
                                string Name = "test";
                                void M(ILogger logger) {
                                    logger.Info({|#0:$"User {this.Name} logged in"|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               string Name = "test";
                               void M(ILogger logger) {
                                   logger.Info("User logged in", new { Name = this.Name });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.InterpolatedStringMessage).WithLocation(0));
    }

    [Fact]
    public async Task MultipleInterpolations_ExtractsAll()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger, string name, int age) {
                                    logger.Info({|#0:$"User {name} age {age}"|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger, string name, int age) {
                                   logger.Info("User age", new { name, age });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.InterpolatedStringMessage).WithLocation(0));
    }

    [Fact]
    public async Task ComplexExpression_UsesFallbackKey()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:$"Result: {1 + 2}"|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   logger.Info("Result:", new { Value1 = 1 + 2 });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.InterpolatedStringMessage).WithLocation(0));
    }
}
