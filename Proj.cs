using System.Reflection;
using HotChocolate.Types.Descriptors;

using System.Text.Json;
using System.Linq.Expressions;

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
			context.Result = new[]
			{
				new { FirstName = "Arad Alvand" },
			};
		});
	}
}

public class AddExprAttribute : ObjectFieldDescriptorAttribute
{
	public AddExprAttribute()
	{

	}

	protected override void OnConfigure(IDescriptorContext context, IObjectFieldDescriptor descriptor, MemberInfo member)
	{
		descriptor.Extend().OnBeforeCreate(d =>
		{
			Expression foo = (Book b) => b.Title.StartsWith("Kir");
			d.ContextData["foo"] = foo;
		});

		// descriptor.Use(next => async context =>
		// {
		// 	context.SetLocalState(Query.ExtraExpressions, new LambdaExpression[]
		// 	{
		// 		(Book b) => b.Title.StartsWith("Kir"),
		// 	});
		// 	// context.ContextData[Query.ExtraExpressions] = new LambdaExpression[] {
		// 	// 	(Author a) => a.FirstName.StartsWith("Kir")
		// 	// };
		// 	await next(context);
		// });
	}
}

// public record Course(
// 	int Id,
// 	string Title,
// 	string MiniTitle,
// 	double AverageRating
// );

// public class CourseType : ObjectType<Course>
// {
// 	protected override void Configure(IObjectTypeDescriptor<Course> descriptor)
// 	{
// 		descriptor.CorrespondsTo<Entities.Course>();

// 		descriptor.Field(c => c.AverageRating)
// 			.Computed()
// 	}
// }
