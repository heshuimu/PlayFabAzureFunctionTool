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

		context.AddSource("Logs", string.Join("\n", collector.Works.Select(a => a.GenerateMethodDelcaration())));
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

				AttributeData? attrSymbol = symbol.GetAttributes()
					.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == typeof(AzureFunctionAttribute).FullName);

				if(attrSymbol == null || !symbol.DeclaredAccessibility.IsAccessibleInSameAssembly() || !symbol.IsAsync)
				{
					return;
				}

				Work work;

				var stringTypeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(string).FullName)!;
				var playfabServerTypeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName("PlayFab.PlayFabServerInstanceAPI")!;

				Console.WriteLine(playfabServerTypeSymbol.ToString());
				Console.WriteLine(stringTypeSymbol.ToString());

				if(!symbol.IsStatic)
				{
					var containingClassSymbol = symbol.ContainingType;

					if(containingClassSymbol.IsAbstract)
					{
						return;
					}

					bool hasParameterlessConstructor = containingClassSymbol.Constructors.Any(c => !c.IsStatic && c.Parameters.IsEmpty);

					bool canSetServer = containingClassSymbol.GetMembers()
						.OfType<IPropertySymbol>()
						.Any(prop => !prop.IsStatic && prop.Name == "Server" && prop.SetMethod != null && SymbolEqualityComparer.Default.Equals(prop.Type, playfabServerTypeSymbol));
					bool canSetCurrentPlayerID = containingClassSymbol.GetMembers()
						.OfType<IPropertySymbol>()
						.Any(prop => !prop.IsStatic && prop.Name == "CurrentPlayerID" && prop.SetMethod != null && SymbolEqualityComparer.Default.Equals(prop.Type, stringTypeSymbol));

					if(hasParameterlessConstructor && canSetServer && canSetCurrentPlayerID)
					{
						work = new InstanceCallWork()
						{
							MethodName = symbol.Name,
							ContainingClassName = containingClassSymbol.ToString(),
							ArgumentTypeName = symbol.Parameters.FirstOrDefault()?.Type.ToString()
						};

						Works.Add(work);
					}
					else
					{
						return;
					}
				}
				else
				{
					if(symbol.Parameters.Length >= 2
					&& SymbolEqualityComparer.Default.Equals(symbol.Parameters[0].Type, playfabServerTypeSymbol)
					&& SymbolEqualityComparer.Default.Equals(symbol.Parameters[1].Type, stringTypeSymbol))
					{
						work = new StaticCallWork()
						{
							MethodName = $"{symbol.ContainingSymbol}.{symbol.Name}",
							ArgumentTypeName = symbol.Parameters.Skip(2).FirstOrDefault()?.Type.ToString(),
						};

						Works.Add(work);
					}
					else
					{
						return;
					}
				}


				work.AzureFunctionName = attrSymbol.NamedArguments.Where(p => p.Key == nameof(AzureFunctionAttribute.Name)).Select(p => (string)p.Value.Value!).FirstOrDefault() ?? symbol.Name;
				work.ReturnTypeName = symbol.ReturnsVoid ? null : symbol.ReturnType.ToString();
				work.DesiredAuthorizationLevel = attrSymbol.NamedArguments.Where(p => p.Key == nameof(AzureFunctionAttribute.AuthorizationLevel)).Select(p => (DummyAuthLevel)p.Value.Value!).FirstOrDefault();
				work.ShouldProvideLogger = false;

				if(work.ReturnTypeName == typeof(Task).FullName)
				{
					work.ReturnTypeName = null;
				}
			}
		}
	}

	private abstract class Work
	{
		public string MethodName { get; set; } = "NONAME";
		public string AzureFunctionName { get; set; } = "NONAME";
		public string? ArgumentTypeName { get; set; }
		public string? ReturnTypeName { get; set; }
		public DummyAuthLevel? DesiredAuthorizationLevel { get; set; }

		public bool ShouldProvideLogger { get; set; }
		public abstract string GenerateMethodDelcaration();
	}

	private class StaticCallWork : Work
	{
		public override string GenerateMethodDelcaration()
		{
			throw new NotImplementedException();
		}

		public override string ToString() => $"static {ReturnTypeName ?? "void"} {MethodName}({ArgumentTypeName})";
	}

	private class InstanceCallWork : Work
	{
		public string ContainingClassName { get; set; } = "NONAME";

		public override string GenerateMethodDelcaration()
		{
			return $@"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PlayFab.Plugins.CloudScript;
using PlayFab;

class GeneratedAzureFunction
{{
	[FunctionName(""{AzureFunctionName}"")]
	public static async Task<IActionResult> {AzureFunctionName}_Generated([HttpTrigger(AuthorizationLevel.{DesiredAuthorizationLevel}, ""post"", Route = null)] HttpRequest req, ILogger log)
	{{
		{(ArgumentTypeName != null ? $"var functionContext = await FunctionContext<{ArgumentTypeName}>.Create(req);" : "var functionContext = await FunctionContext.Create(req);")}

		PlayFabServerInstanceAPI server = new(functionContext.ApiSettings, functionContext.AuthenticationContext);
		string currentPlayerID = functionContext.CurrentPlayerId;

		{(ReturnTypeName != null ? "var result = " : null)}await new {ContainingClassName}()
		{{
			Server = server,
			CurrentPlayerID = currentPlayerID
		}}.{MethodName}({(ArgumentTypeName != null ? "functionContext.FunctionArgument" : null)});


		{(ReturnTypeName != null ? "return new OkObjectResult(result);" : "return new OkResult();")}
	}}
}}
			";
		}

		public override string ToString() => $"{ReturnTypeName ?? "void"} {MethodName}({ArgumentTypeName})";
	}
}

internal static class AccessibilityExtension
{
	public static bool IsAccessibleInSameAssembly(this Accessibility a) => a == Accessibility.Internal || a == Accessibility.Public;
}