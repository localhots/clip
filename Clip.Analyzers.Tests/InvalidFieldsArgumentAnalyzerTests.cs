using Verify = Clip.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Clip.Analyzers.InvalidFieldsArgumentAnalyzer>;

namespace Clip.Analyzers.Tests;

public class InvalidFieldsArgumentAnalyzerTests
{
    [Fact]
    public async Task AnonymousObject_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", new { Key = 42 });
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NullFields_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", null);
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoFields_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg");
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Dictionary_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            using System.Collections.Generic;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", new Dictionary<string, object?> { ["Key"] = 42 });
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IntLiteral_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", {|#0:42|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument).WithLocation(0).WithArguments("int"));
    }

    [Fact]
    public async Task StringLiteral_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", {|#0:"bad"|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument).WithLocation(0).WithArguments("string"));
    }

    [Fact]
    public async Task BoolLiteral_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", {|#0:true|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument).WithLocation(0).WithArguments("bool"));
    }

    [Fact]
    public async Task Array_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    logger.Info("msg", {|#0:new[] { 1, 2, 3 }|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument).WithLocation(0).WithArguments("int[]"));
    }

    [Fact]
    public async Task Enum_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                enum Color { Red, Green }
                                void M(ILogger logger) {
                                    logger.Info("msg", {|#0:Color.Red|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument).WithLocation(0).WithArguments("Color"));
    }

    [Fact]
    public async Task AddContext_IntLiteral_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger) {
                                    using (logger.AddContext({|#0:42|})) { }
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument).WithLocation(0).WithArguments("int"));
    }

    [Fact]
    public async Task MethodCall_ReturningPrimitive_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                int GetCount() => 42;
                                void M(ILogger logger) {
                                    logger.Info("msg", {|#0:GetCount()|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument).WithLocation(0).WithArguments("int"));
    }

    [Fact]
    public async Task TernaryExpression_BothBranchesString_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger, bool flag) {
                                    logger.Info("msg", {|#0:flag ? "yes" : "no"|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument).WithLocation(0).WithArguments("string"));
    }

    [Fact]
    public async Task ArrayIndexer_PrimitiveElement_Diagnostic()
    {
        const string test = """
                            using Clip;
                            class C {
                                void M(ILogger logger, int[] arr) {
                                    logger.Info("msg", {|#0:arr[0]|});
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(DiagnosticIds.InvalidFieldsArgument).WithLocation(0).WithArguments("int"));
    }

    [Fact]
    public async Task ErrorWithException_NoDiagnostic()
    {
        const string test = """
                            using Clip;
                            using System;
                            class C {
                                void M(ILogger logger, Exception ex) {
                                    logger.Error("failed", ex, new { Op = "save" });
                                }
                            }
                            """;
        await Verify.VerifyAnalyzerAsync(test);
    }
}
