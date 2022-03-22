

namespace K4.AzureFunctions;

public enum DummyAuthLevel
{
	NotSpecified = 0,
	Function,
	Anonymous
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public class DefaultAuthorizationLevelAttribute : Attribute
{
	public DummyAuthLevel AuthorizationLevel { get; }

	public DefaultAuthorizationLevelAttribute(DummyAuthLevel authorizationLevel)
	{
		AuthorizationLevel = authorizationLevel;
	}
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AzureFunctionAttribute : Attribute
{
	public DummyAuthLevel AuthorizationLevel { get; set; }
	public string? Name { get; set; }
}