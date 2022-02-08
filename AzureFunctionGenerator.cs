using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace  K4.AzureFunctions;

[Generator]
class AzureFunctionGenerator : ISourceGenerator
{
	public void Execute(GeneratorExecutionContext context)
	{
		var defaultAuthLevelAttr = context.Compilation.Assembly.GetAttributes().FirstOrDefault(
			attr => attr.AttributeClass?.Name == nameof(DefaultAuthorizationLevelAttribute)
		);

		if(defaultAuthLevelAttr == null)
		{
			throw new InvalidOperationException($"{nameof(DefaultAuthorizationLevelAttribute)} is not defined in your assembly!");
		}

		var defaultAuthLevel = (DummyAuthLevel)defaultAuthLevelAttr.ConstructorArguments.First().Value!;

		if(context.SyntaxContextReceiver is not WorkCollector collector)
		{
			return;
		}

		context.AddSource("Logs", string.Join("\n", collector.Works.Select(a => a.ToString())));
	}

	public void Initialize(GeneratorInitializationContext context)
	{
		context.RegisterForSyntaxNotifications(() => new WorkCollector());
	}

	private class WorkCollector : ISyntaxContextReceiver
	{
		public List<Work> Works { get; private set; } = new();

		public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
		{
			if(context.Node is MethodDeclarationSyntax methodDeclaration)
			{
				var symbol = (IMethodSymbol)context.SemanticModel.GetDeclaredSymbol(methodDeclaration)!;

				if(!symbol.IsStatic && !symbol.DeclaredAccessibility.IsAccessibleInSameAssembly())
				{
					return;
				}

				AttributeData? attrSymbol = symbol.GetAttributes()
					.Where(a => a.AttributeClass?.ToDisplayString() == typeof(AzureFunctionAttribute).FullName)
					.FirstOrDefault();

				if(attrSymbol == null)
				{
					return;
				}

				Work work = new()
				{
					MethodName = $"{symbol.ContainingSymbol}.{symbol.Name}",
					AzureFunctionName = attrSymbol.NamedArguments.Where(p => p.Key == nameof(AzureFunctionAttribute.Name)).Select(p => (string)p.Value.Value!).FirstOrDefault(),
					ArgumentTypeName = symbol.Parameters.Skip(1).FirstOrDefault()?.Type.ToString(),
					ReturnTypeName = symbol.ReturnsVoid ? null : symbol.ReturnType.ToString(),
					DesiredAuthorizationLevel = attrSymbol.NamedArguments.Where(p => p.Key == nameof(AzureFunctionAttribute.AuthorizationLevel)).Select(p => (DummyAuthLevel)p.Value.Value!).FirstOrDefault(),
					ShouldProvideLogger = false
				};

				Works.Add(work);
			}
		}
	}

	private class Work
	{
		public string MethodName { get; set; } = "NONAME";
		public string? AzureFunctionName { get; set; }
		public string? ArgumentTypeName { get; set; }
		public string? ReturnTypeName { get; set; }
		public DummyAuthLevel? DesiredAuthorizationLevel { get; set; }

		public bool ShouldProvideLogger { get; set; }

		public override string ToString() => $"{ReturnTypeName ?? "void"} {MethodName}({ArgumentTypeName})";
	}
}

internal static class AccessibilityExtension
{
	public static bool IsAccessibleInSameAssembly(this Accessibility a) => a == Accessibility.Internal || a == Accessibility.Public;
}