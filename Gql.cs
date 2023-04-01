using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;
using HotChocolate.Resolvers;
using AgileObjects.ReadableExpressions;

namespace hc_ef_custom.Types;

[QueryType]
public static class Query
{
	[UseSingleOrDefault]
	public static IQueryable<Author>? GetAuthor(
		AppDbContext db,
		IResolverContext context
	)
	{
		// var result = db.Books.Select(b => new
		// {
		// 	Foo = b.Foo(5),
		// }).ToList();
		// Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

		var selections = context.GetSelections((IObjectType)context.Selection.Type);

		var param = Expression.Parameter(typeof(Author));
		var propertyAccesses = selections.Select(s =>
			Expression.Convert(
				Expression.Property(param, (PropertyInfo)s.Field.Member!),
				typeof(object)
			)
		);
		var arrayInit = Expression.NewArrayInit(typeof(object), propertyAccesses);
		var lambda = (Expression<Func<Author, object[]>>)Expression.Lambda(arrayInit, param);

		var result = db.Authors.Select(lambda).ToList();

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine(lambda.ToReadableString());
		Console.ForegroundColor = ConsoleColor.Magenta;
		Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
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
