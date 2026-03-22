using Verify = Clip.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<
    Clip.Analyzers.MessageTemplateSyntaxAnalyzer,
    Clip.Analyzers.MessageTemplateSyntaxCodeFix>;

namespace Clip.Analyzers.Tests;

public class MessageTemplateSyntaxCodeFixTests
{
    [Fact]
    public async Task RawValue_PairsWithPlaceholder()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:"User {UserId} logged in"|}, 123);
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   logger.Info("User logged in", new { UserId = 123 });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.MessageTemplateSyntax)
                .WithLocation(0)
                .WithArguments("UserId"));
    }

    [Fact]
    public async Task ExistingAnonymousType_JustCleansMessage()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:"User {UserId} logged in"|}, new { UserId = 123 });
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   logger.Info("User logged in", new { UserId = 123 });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.MessageTemplateSyntax)
                .WithLocation(0)
                .WithArguments("UserId"));
    }

    [Fact]
    public async Task NoFields_CreatesPlaceholderFields()
    {
        const string test = """
                            using Clip;
                            class C {
                                string Name = "test";
                                void M(ILogger logger) {
                                    logger.Info({|#0:"User {Name} logged in"|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               string Name = "test";
                               void M(ILogger logger) {
                                   logger.Info("User logged in", new { Name = Name });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.MessageTemplateSyntax)
                .WithLocation(0)
                .WithArguments("Name"));
    }

    [Fact]
    public async Task MultiplePlaceholders_ExistingAnonymousType_CleansMessage()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:"User {UserId} in {Module}"|}, new { UserId = 123, Module = "admin" });
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   logger.Info("User in", new { UserId = 123, Module = "admin" });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.MessageTemplateSyntax)
                .WithLocation(0)
                .WithArguments("UserId"));
    }

    [Fact]
    public async Task RawIdentifier_PairsWithPlaceholder()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger, string userId) {
                                    logger.Info({|#0:"User {UserId} logged in"|}, userId);
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger, string userId) {
                                   logger.Info("User logged in", new { UserId = userId });
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.MessageTemplateSyntax)
                .WithLocation(0)
                .WithArguments("UserId"));
    }
}
