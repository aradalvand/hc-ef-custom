using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;
using HotChocolate.Resolvers;

namespace hc_ef_custom.Types;

[QueryType]
public static class Query
{
	[UseTest]
	public static IQueryable<Author>? GetAuthor(
		AppDbContext db,
		IResolverContext context
	)
	{
		var result = db.Books.Select(b => new
		{
			Foo = b.Foo(5),
		}).ToList();
		Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
		// Console.ForegroundColor = ConsoleColor.Cyan;

		// var selections = context.GetSelections((IObjectType)context.Selection.Type);
		// var selectedFields = selections.Select(s => s.Field);
		// foreach (var field in selectedFields)
		// {
		// 	Console.WriteLine($"Name: {field.Name}");
		// 	Console.WriteLine($"Member: {field.Member}");
		// 	Console.WriteLine($"field.ContextData[...]: {field.ContextData.GetValueOrDefault("Expression")}");
		// 	Console.WriteLine($"Type: {field.Type}");
		// 	Console.WriteLine("---");
		// }

		//Expression.MemberBind()
		// Expression.Lambda()

		// var res = db.Authors.Project(context);
		// Console.ForegroundColor = ConsoleColor.Cyan;
		// Console.WriteLine(res.Expression);
		return null;
	}
}

// public class AuthorType : ObjectType<Author>
// {
// 	protected override void Configure(IObjectTypeDescriptor<Author> descriptor)
// 	{
// 		// descriptor.Ignore();

// 		// descriptor.Field("fullName")
// 		// 	.Type<NonNullType<StringType>>()
// 		// 	.Computed(() => "Foo Bar");
// 	}
// }


public static class ObjectFieldDescriptorExtensions
{
	public static IObjectFieldDescriptor Computed<TValue>(
		this IObjectFieldDescriptor descriptor,
		Expression<Func<TValue>> expr
	)
	{
		descriptor.Extend().OnBeforeCreate(d =>
		{
			d.ContextData["Expression"] = expr; // https://github.com/ChilliCream/graphql-platform/blob/6e9b7a9936f36f300903b764c0a3d39d5e67347a/src/HotChocolate/Data/src/Data/Projections/Extensions/ProjectionObjectFieldDescriptorExtensions.cs#L52
		});
		descriptor.Type(typeof(TValue));
		descriptor.Resolve(expr);

		return descriptor;
	}
}
