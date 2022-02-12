# AzureFunctionTool

Generate Azure Functions boilerplate code to aid PlayFab server functions development

***Note: These should work in theory. I have not actually tested the generated code. Use it at your own discretion.***

***If you have any issues or feature requests, I won't be able to add it quickly, so feel free to fix/add it yourself. Pull request is optional and always appreciated***

## Motivation

PlayFab used to have a JavaScript-based server-side scripting system called CloudScript. Some time after the Microsoft aquisition, they implemented Azure Function hooks and, in favor of it, deprecated the JS system. Although the Azure Functions system carries the "CloudScript" brand name, [it is not comparable with the previous offerings](https://community.playfab.com/questions/59814/azure-functions-integration-is-very-unintuitive-as.html).

While running server-side logic on Azure Functions has its own advantages, the ease of use of previous system was not maintained. At the time of writing PlayFab did not seem to provide enough mediations to the heightened learning curves and setup complexity.

This tool aims to resolve one aspect of the problem by using C# Source Generator to generate much of the boilerplate code that Azure Functions requires, but has no relation to actual PlayFab server development. The setup aims to minic the development experience that the previous JS system provides, with pre-populated current user ID, server API instance, and logger (provided by Azure Functions).

## Usage

***These steps assume you have a C# project ready with Azure Functions and PlayFab SDK included.***

Pull or download this repository. In your C# project file (.csproj), include the tool project under the same `ItemGroup` node as other dependencies:
```xml
<ProjectReference Include="../AzureFunctionTool/AzureFunctionTool.csproj" OutputItemType="Analyzer" />
```

Then, enable source generation under `PropertyGroup`:
```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
```

In any C# file, preferrably AssemblyInfo.cs (create one if you have not), add the following to declare the default access level of generated Azure Functions. Currently, `Function` and `Anonymous` are available:
```cs
using K4.AzureFunctions;

[assembly: DefaultAuthorizationLevel(DummyAuthLevel.Function)]
```

Now, you can write methods for PlayFab logic. There are two styles available:

1. Write the method as instance method in a class with two settable properties `PlayFabServerInstanceAPI Server` and `string CurrentPlayerID`.

1. Write the method as static method in any class. The first two parameters must be of type `PlayFabServerInstanceAPI`  and `string`, for server API instance and current player ID, respectively.

Then annotate the method with `[AzureFunction]`. The boilerplate will be generated on the next build.

**Note:**
- Both async and sync methods are supported.

- Return types can be anything as long as they are serializable by Azure Functions. Returning `void` is allowed.

- The first method parameters is treated as the input. The boilerplate will deserialize the JSON object in the HTTP request body into the type of that parameter.

- If the last method parameter is of type `ILogger`, the boilerplate will pass the Azure Functions logger instance to you via the parameter.

Refer to the following sample code:
```cs
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayFab;
using K4.AzureFunctions;

public class DummyClass
{
	public PlayFabServerInstanceAPI Server { get; init; }
	public string CurrentPlayerID { get; init; }

	public class DummyInput
	{
	}

	[AzureFunction(Name = "abc", AuthorizationLevel = DummyAuthLevel.Anonymous)]
	public async Task<int> DummyInstanceMethod(DummyInput input)
	{
		return 1;
	}

	[AzureFunction]
	public async Task DummVoidMethod(DummyInput input, ILogger log)
	{
	}

	[AzureFunction]
	public static void DummyStaticMethod(PlayFabServerInstanceAPI server, string currentPlayerID)
	{
	}
}
```

Generated code:
```cs
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PlayFab.Plugins.CloudScript;
using PlayFab;

class GeneratedAzureFunction
{
	[FunctionName("abc")]
	public static async Task<IActionResult> abc_Generated([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log)
	{
		var functionContext = await FunctionContext<DummyClass.DummyInput>.Create(req);

		PlayFabServerInstanceAPI server = new(functionContext.ApiSettings, functionContext.AuthenticationContext);
		string currentPlayerID = functionContext.CurrentPlayerId;

		var result = await new DummyClass(){ Server = server, CurrentPlayerID = currentPlayerID }.DummyInstanceMethod(functionContext.FunctionArgument);

		return new OkObjectResult(result);
	}

	[FunctionName("DummVoidMethod")]
	public static async Task<IActionResult> DummVoidMethod_Generated([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log)
	{
		var functionContext = await FunctionContext<DummyClass.DummyInput>.Create(req);

		PlayFabServerInstanceAPI server = new(functionContext.ApiSettings, functionContext.AuthenticationContext);
		string currentPlayerID = functionContext.CurrentPlayerId;

		await new DummyClass(){ Server = server, CurrentPlayerID = currentPlayerID }.DummVoidMethod(functionContext.FunctionArgument, log);

		return new OkResult();
	}

	[FunctionName("DummyStaticMethod")]
	public static async Task<IActionResult> DummyStaticMethod_Generated([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log)
	{
		var functionContext = await FunctionContext.Create(req);

		PlayFabServerInstanceAPI server = new(functionContext.ApiSettings, functionContext.AuthenticationContext);
		string currentPlayerID = functionContext.CurrentPlayerId;

		DummyClass.DummyStaticMethod(server, currentPlayerID);

		return new OkResult();
	}
}
```

## Limitations

1. This library is only for PlayFab development. It is not suitable for general Azure Functions development.

2. Exceptions from business logics are currently uncaught.