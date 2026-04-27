using Verify = Clip.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Clip.Analyzers.MessageTemplateSyntaxAnalyzer>;

namespace Clip.Analyzers.Tests;

public class MessageTemplateSyntaxAnalyzerTests
{
    [Fact]
    public async Task PlainMessage_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("Request handled");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EscapedBraces_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("JSON: {{key}}");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NumericPlaceholder_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("Value is {0}");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TemplateSyntax_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:"User {UserId} logged in"|}, new { UserId = 42 });
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.MessageTemplateSyntax).WithLocation(0).WithArguments("UserId"));
    }

    [Fact]
    public async Task MultipleTemplates_ReportsFirst()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:"User {UserId} at {Path}"|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.MessageTemplateSyntax).WithLocation(0).WithArguments("UserId"));
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
    public async Task EmptyBraces_NoDiagnostic()
    {
        // {} has no identifier — must not match the placeholder pattern.
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("Curly braces: {}");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LoneOpenBrace_NoDiagnostic()
    {
        // Unmatched `{` with no closing brace must not be flagged.
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("Trailing brace: {prefix");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EscapedBracesAroundIdentifier_NoDiagnostic()
    {
        // `{{x}}` is the C# composite-format escape — renders as literal `{x}` and is not
        // a template placeholder. The regex's negative lookarounds must reject it.
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("Escaped: {{x}}");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }
}
