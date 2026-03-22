using Verify = Clip.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<
    Clip.Analyzers.InvalidFieldsArgumentAnalyzer,
    Clip.Analyzers.InvalidFieldsArgumentCodeFix>;

namespace Clip.Analyzers.Tests;

public class InvalidFieldsArgumentCodeFixTests
{
    [Fact]
    public async Task IntLiteral_WrapsWithValueKey()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", {|#0:42|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   logger.Info("msg", new { value = 42 });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument)
                .WithLocation(0)
                .WithArguments("int"));
    }

    [Fact]
    public async Task Identifier_WrapsWithShorthand()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    int count = 5;
                                    logger.Info("msg", {|#0:count|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   int count = 5;
                                   logger.Info("msg", new { count });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument)
                .WithLocation(0)
                .WithArguments("int"));
    }

    [Fact]
    public async Task StringLiteral_WrapsWithValueKey()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", {|#0:"hello"|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   logger.Info("msg", new { value = "hello" });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument)
                .WithLocation(0)
                .WithArguments("string"));
    }

    [Fact]
    public async Task BoolLiteral_WrapsWithValueKey()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", {|#0:true|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   logger.Info("msg", new { value = true });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument)
                .WithLocation(0)
                .WithArguments("bool"));
    }
}
