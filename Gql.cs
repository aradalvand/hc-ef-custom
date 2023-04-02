using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;
using HotChocolate.Resolvers;
using AgileObjects.ReadableExpressions;
using Microsoft.EntityFrameworkCore.Query;

namespace hc_ef_custom.Types;

[QueryType]
public static class Query
{
	[UseSingleOrDefault]
	[AddExpr]
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
		var arrayInitializers = new List<Expression>();
		arrayInitializers.AddRange(selections.Select(s =>
			Expression.Convert(
				Expression.Property(param, (PropertyInfo)s.Field.Member!),
				typeof(object)
			)
		));

		Console.WriteLine(JsonSerializer.Serialize(context.ContextData.Keys, new JsonSerializerOptions { WriteIndented = true }));
		if (context.ContextData.TryGetValue("Expr2", out var exprObj) && exprObj is LambdaExpression expr)
		{
			Console.WriteLine(expr.ToReadableString());
			var ready = ReplacingExpressionVisitor.Replace(expr.Parameters.First(), param, expr.Body);
			Console.WriteLine(ready.ToReadableString());
			arrayInitializers.Add(Expression.Convert(ready, typeof(object)));
		}

		var arrayNew = Expression.NewArrayInit(typeof(object), arrayInitializers);
		var lambda = (Expression<Func<Author, object[]>>)Expression.Lambda(arrayNew, param);

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine(lambda.ToReadableString());
		var result = db.Authors.Select(lambda).ToList();
		Console.ForegroundColor = ConsoleColor.Magenta;
		Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

		return null;
	}
}

public class AuthorType : ObjectType<Author>
{
	protected override void Configure(IObjectTypeDescriptor<Author> descriptor)
	{
		// descriptor.Ignore();

		// descriptor.Field("fullName")
		// 	.Type<NonNullType<StringType>>()
		// 	.Computed(() => "Foo Bar");

		descriptor.Field(a => a.FullName).Computed(() => "");
	}
}


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
		descriptor.Resolve(ctx => expr.Compile().Invoke());

		return descriptor;
	}
}
