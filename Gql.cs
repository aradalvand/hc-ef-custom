using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;
using HotChocolate.Resolvers;
using AgileObjects.ReadableExpressions;
using Microsoft.EntityFrameworkCore.Query;
using HotChocolate.Execution.Processing;

namespace hc_ef_custom.Types;

[QueryType]
public static class Query
{
	public const string ExtraExpressions = "ExtraExpressions";
	[UseSingleOrDefault]
	[AddExpr]
	public static IQueryable<Book>? GetBook(
		AppDbContext db,
		IResolverContext context
	)
	{
		// var result = db.Books.Select(b => new
		// {
		// 	Foo = b.Foo(5),
		// }).ToList();
		// Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

		var topSelection = context.GetSelections((IObjectType)context.Selection.Type.NamedType());
		var param = Expression.Parameter(typeof(Book));

		IEnumerable<Expression> Project(IEnumerable<ISelection> selections, Expression on)
		{
			var expressions = new List<Expression>();

			foreach (var selection in selections)
			{
				var property = (PropertyInfo)selection.Field.Member!;
				var propertyExpr = Expression.Property(
					on,
					property
				);

				if (selection.SelectionSet is null)
				{
					expressions.Add(propertyExpr);
				}
				else
				{
					var objectType = (IObjectType)selection.Type.NamedType();
					var innerSelections = context.GetSelections(
						objectType,
						selection
					);
					if (selection.Type.IsListType())
					{
						// TODO: Duplicated outside
						var param = Expression.Parameter(objectType.RuntimeType);
						var arrayInit = Expression.NewArrayInit(
							typeof(object),
							Project(innerSelections, param).Select(e => Expression.Convert(e, typeof(object)))
						);
						var lambda = Expression.Lambda(arrayInit, param);
						var select = Expression.Call(
							typeof(Enumerable),
							nameof(Enumerable.Select),
							new Type[] { objectType.RuntimeType, lambda.Body.Type },
							propertyExpr, lambda // NOTE: `propertyExpr` here is what gets passed to `Select` as its `this` argument, and `lambda` is the lambda that gets passed to it.
						);
						expressions.Add(select);
					}
					else
					{
						expressions.AddRange(Project(innerSelections, propertyExpr));
					}
				}
			}

			return expressions; // NOTE: Necessary — see https://stackoverflow.com/a/2200247/7734384
		}

		// var extraExpressions = context.GetLocalStateOrDefault<IEnumerable<LambdaExpression>>(ExtraExpressions);
		// if (extraExpressions is not null)
		// {
		// 	foreach (var expr in extraExpressions)
		// 	{
		// 		expressions.Add(ReplacingExpressionVisitor.Replace(
		// 			expr.Parameters.First(),
		// 			param,
		// 			expr.Body
		// 		));
		// 	}
		// }

		var arrayNew = Expression.NewArrayInit(
			typeof(object),
			Project(topSelection, param).Select(e => Expression.Convert(e, typeof(object)))
		);
		var lambda = (Expression<Func<Book, object[]>>)Expression.Lambda(arrayNew, param);

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine(lambda.ToReadableString());
		var result = db.Books.Select(lambda).ToList();
		Console.ForegroundColor = ConsoleColor.Magenta;
		Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

		return null;
	}

	private static void PrettyPrint(object? obj)
	{
		// Console.WriteLine(
		// 	JsonSerializer.Serialize(obj, new JsonSerializerOptions
		// 	{
		// 		WriteIndented = true
		// 	})
		// );
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
