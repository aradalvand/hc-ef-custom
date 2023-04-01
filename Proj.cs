using System.Reflection;
using HotChocolate.Types.Descriptors;

using System.Text.Json;

namespace hc_ef_custom.Types;

public class UseTestAttribute : ObjectFieldDescriptorAttribute
{
	public UseTestAttribute()
	{

	}

	protected override void OnConfigure(IDescriptorContext context, IObjectFieldDescriptor descriptor, MemberInfo member)
	{
		descriptor.Use(next => async context =>
		{
			await next(context);
			var s = JsonSerializer.Serialize(context.Result, new JsonSerializerOptions { WriteIndented = true });
			Console.WriteLine($"context.Result: {s}");
		});
	}
}
