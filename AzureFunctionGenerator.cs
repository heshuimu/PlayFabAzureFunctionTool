using System.Text;
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

		StringBuilder builder = new();

		builder.Append(
@"using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PlayFab.Plugins.CloudScript;
using PlayFab;

class GeneratedAzureFunction
{"
		);

		foreach(Work w in collector.Works)
		{
			builder.Append(w.GenerateMethodDelcaration());
		}

		builder.Append("}");

		context.AddSource("AzureFunctions.g", string.Join("\n", builder.ToString()));
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
				var method = (IMethodSymbol)context.SemanticModel.GetDeclaredSymbol(methodDeclaration)!;

				AttributeData? azureFunctionAttribute = method.GetAttributes()
					.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == typeof(AzureFunctionAttribute).FullName);

				if(azureFunctionAttribute == null || !method.DeclaredAccessibility.IsAccessibleInSameAssembly())
				{
					return;
				}

				Work work;

				var stringType = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(string).FullName)!;
				var taskType = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName)!;
				var loggerType = context.SemanticModel.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger")!;
				var playfabServerType = context.SemanticModel.Compilation.GetTypeByMetadataName("PlayFab.PlayFabServerInstanceAPI")!;

				Console.WriteLine(playfabServerType.ToString());
				Console.WriteLine(stringType.ToString());

				if(!method.IsStatic)
				{
					var containingClassSymbol = method.ContainingType;

					if(containingClassSymbol.IsAbstract)
					{
						return;
					}

					bool hasParameterlessConstructor = containingClassSymbol.Constructors.Any(c => !c.IsStatic && c.Parameters.IsEmpty);

					bool canSetServer = containingClassSymbol.GetMembers()
						.OfType<IPropertySymbol>()
						.Any(prop => !prop.IsStatic && prop.Name == "Server" && prop.SetMethod != null && SymbolEqualityComparer.Default.Equals(prop.Type, playfabServerType));
					bool canSetCurrentPlayerID = containingClassSymbol.GetMembers()
						.OfType<IPropertySymbol>()
						.Any(prop => !prop.IsStatic && prop.Name == "CurrentPlayerID" && prop.SetMethod != null && SymbolEqualityComparer.Default.Equals(prop.Type, stringType));

					if(hasParameterlessConstructor && canSetServer && canSetCurrentPlayerID)
					{
						work = new InstanceCallWork()
						{
							MethodName = method.Name,
							ContainingClassName = containingClassSymbol.ToString(),
							ArgumentTypeName = method.Parameters.FirstOrDefault()?.Type.ToString()
						};
					}
					else
					{
						return;
					}
				}
				else
				{
					if(method.Parameters.Length >= 2
					&& SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, playfabServerType)
					&& SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, stringType))
					{
						work = new StaticCallWork()
						{
							MethodName = $"{method.ContainingSymbol}.{method.Name}",
							ArgumentTypeName = method.Parameters.Skip(2).FirstOrDefault()?.Type.ToString()
						};
					}
					else
					{
						return;
					}
				}

				work.IsAsyncCall = method.IsAsync;
				work.AzureFunctionName = azureFunctionAttribute.NamedArguments.Where(p => p.Key == nameof(AzureFunctionAttribute.Name)).Select(p => (string)p.Value.Value!).FirstOrDefault() ?? method.Name;
				work.ReturnTypeName = method.ReturnsVoid || SymbolEqualityComparer.Default.Equals(method.ReturnType, taskType) ? null : method.ReturnType.ToString();
				work.DesiredAuthorizationLevel = azureFunctionAttribute.NamedArguments.Where(p => p.Key == nameof(AzureFunctionAttribute.AuthorizationLevel)).Select(p => (DummyAuthLevel)p.Value.Value!).FirstOrDefault();
				work.ShouldProvideLogger = SymbolEqualityComparer.Default.Equals(method.Parameters.Last().Type, loggerType);

				Works.Add(work);
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

		public bool IsAsyncCall { get; set; }
		public bool ShouldProvideLogger { get; set; }

		public abstract string GenerateMethodCall();

		public string GenerateMethodDelcaration() =>
$@"
	[FunctionName(""{AzureFunctionName}"")]
	public static async Task<IActionResult> {AzureFunctionName}_Generated([HttpTrigger(AuthorizationLevel.{DesiredAuthorizationLevel}, ""post"", Route = null)] HttpRequest req, ILogger log)
	{{
		{(ArgumentTypeName != null ? $"var functionContext = await FunctionContext<{ArgumentTypeName}>.Create(req);" : "var functionContext = await FunctionContext.Create(req);")}

		PlayFabServerInstanceAPI server = new(functionContext.ApiSettings, functionContext.AuthenticationContext);
		string currentPlayerID = functionContext.CurrentPlayerId;

		{(ReturnTypeName != null ? "var result = " : null)}{(IsAsyncCall ? "await " : null)}{GenerateMethodCall()};

		{(ReturnTypeName != null ? "return new OkObjectResult(result);" : "return new OkResult();")}
	}}
";

		public virtual IEnumerable<string> GetCommonMethodArguments()
		{
			List<string> arguments = new();

			if(ArgumentTypeName != null)
			{
				arguments.Add("functionContext.FunctionArgument");
			}

			if(ShouldProvideLogger)
			{
				arguments.Add("log");
			}

			return arguments;
		}
	}

	private class StaticCallWork : Work
	{
		public override string GenerateMethodCall() => $"{MethodName}({string.Join(", ", new string[]{"server", "currentPlayerID"}.Concat(GetCommonMethodArguments()))})";
	}

	private class InstanceCallWork : Work
	{
		public string ContainingClassName { get; set; } = "NONAME";

		public override string GenerateMethodCall() => $"new {ContainingClassName}(){{ Server = server, CurrentPlayerID = currentPlayerID }}.{MethodName}({string.Join(", ", GetCommonMethodArguments())})";
	}
}

internal static class AccessibilityExtension
{
	public static bool IsAccessibleInSameAssembly(this Accessibility a) => a == Accessibility.Internal || a == Accessibility.Public;
}