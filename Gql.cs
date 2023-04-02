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
	public const string AuthContextKey = "Auth";

	public static async Task<Book?> GetBook(
		AppDbContext db,
		IResolverContext context,
		int id
	)
	{
		await db.Books.Where(b => b.Id == id)
			.ProjectCustom(context, ResultType.Single);

		return null;
	}

	public static async Task<Author?> GetAuthor(
		AppDbContext db,
		IResolverContext context,
		int id
	)
	{
		await db.Authors.Where(b => b.Id == id)
			.ProjectCustom(context, ResultType.Single);

		return null;
	}
}

public static class QueryableExtensions
{
	public static async Task ProjectCustom<T>(
		this IQueryable<T> query,
		IResolverContext context,
		ResultType resultType
	)
	{
		var topSelection = context.GetSelections((IObjectType)context.Selection.Type.NamedType());
		var param = Expression.Parameter(typeof(T));

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
						var select = Expression.Call( // NOTE: https://stackoverflow.com/a/51896729
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

				if (selection.Field.ContextData.GetValueOrDefault(Query.AuthContextKey) is IEnumerable<AuthRule<T>> authRules)
				{
					foreach (var (rule, _) in authRules.Where(r => r.ShouldApply?.Invoke(selection) ?? true))
					{
						expressions.Add(ReplacingExpressionVisitor.Replace(
							rule.Parameters.First(),
							param,
							rule.Body
						));
					}
				}
			}

			return expressions;
		}

		var arrayNew = Expression.NewArrayInit(
			typeof(object),
			Project(topSelection, param).Select(e => Expression.Convert(e, typeof(object))) // NOTE: Necessary — see https://stackoverflow.com/a/2200247/7734384
		);
		var lambda = (Expression<Func<T, object[]>>)Expression.Lambda(arrayNew, param);

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine($"EXPRESSION: {lambda.ToReadableString()}");

		var selectedQuery = query.Select(lambda);
		var result = await selectedQuery.FirstOrDefaultAsync();

		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine($"RESULT: {JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })}");
		Console.ResetColor();

		Console.WriteLine("----------");
	}
}

public enum ResultType
{
	Single,
	Multiple
}

public class BookType : ObjectType<Book>
{
	protected override void Configure(IObjectTypeDescriptor<Book> descriptor)
	{
		// descriptor.Ignore();

		// descriptor.Field("fullName")
		// 	.Type<NonNullType<StringType>>()
		// 	.Computed(() => "Foo Bar");

		descriptor.Field(a => a.Title)
			.Auth(b => b.Title.StartsWith("Foo"));
	}
}

public static class ObjectFieldDescriptorExtensions
{
	public static IObjectFieldDescriptor Auth(
		this IObjectFieldDescriptor descriptor,
		Expression<Func<Book, bool>> ruleExpr
	)
	{
		AuthRule<Book> rule = new(ruleExpr);
		descriptor.Extend().OnBeforeCreate(d =>
		{
			if (d.ContextData.GetValueOrDefault(Query.AuthContextKey) is List<AuthRule<Book>> authRules)
				authRules.Add(rule);
			else
				d.ContextData[Query.AuthContextKey] = new List<AuthRule<Book>> { rule };
		});
		descriptor.Use(next => async context =>
		{
			await next(context);
			// TODO: Check the results
		});

		return descriptor;
	}
}

public record AuthRule<T>(
	Expression<Func<T, bool>> Rule,
	Func<ISelection, bool>? ShouldApply = null
);
