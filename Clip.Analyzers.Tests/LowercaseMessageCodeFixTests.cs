using Verify = Clip.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<
    Clip.Analyzers.LowercaseMessageAnalyzer,
    Clip.Analyzers.LowercaseMessageCodeFix>;

namespace Clip.Analyzers.Tests;

public class LowercaseMessageCodeFixTests
{
    [Fact]
    public async Task LowercaseMessage_Capitalizes()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:"user logged in"|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   logger.Info("User logged in");
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.LowercaseMessage).WithLocation(0));
    }

    [Fact]
    public async Task SingleChar_Capitalizes()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info({|#0:"x"|});
                                }
                            }
                            """;
        const string fix = """
                           using Clip;
                           class C {
                               void M(ILogger logger) {
                                   logger.Info("X");
                               }
                           }
                           """;
        await Verify.VerifyCodeFixAsync(test, fix,
            Verify.Diagnostic(DiagnosticIds.LowercaseMessage).WithLocation(0));
    }
}
