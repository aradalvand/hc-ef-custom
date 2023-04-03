using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Resolvers;
using AgileObjects.ReadableExpressions;
using HotChocolate.Execution.Processing;
using System.Text.Json;
using hc_ef_custom.Types;
using Microsoft.EntityFrameworkCore.Query;

namespace hc_ef_custom;

public class CustomProjectionMiddleware
{
	public const string MetaContextKey = "Meta";

	private readonly FieldDelegate _next;

	public CustomProjectionMiddleware(FieldDelegate next)
	{
		_next = next;
	}

	public async Task Invoke(IMiddlewareContext context)
	{
		await _next(context);
		if (context.Result is not IQueryable<Book> query)
			throw new InvalidOperationException();

		Console.WriteLine($"query.ElementType: {query.ElementType}");

		Dictionary<Type, Type> typeDict = new()
		{
			[typeof(BookDto)] = typeof(Book),
			[typeof(AuthorDto)] = typeof(Author),
			[typeof(BookRatingDto)] = typeof(BookRating),
		};

		var type = (IObjectType)context.Selection.Type.NamedType();
		var topLevelSelections = context.GetSelections(type);

		MemberInitExpression Project(
			IEnumerable<ISelection> selections,
			Expression sourceExpr
		)
		{
			List<MemberAssignment> assignments = new();
			Dictionary<string, Expression>? metaExprs = null;

			var dtoType = selections.First().Field.DeclaringType.RuntimeType;
			Console.WriteLine($"dtoType: {dtoType}");
			var entityType = typeDict[dtoType];
			Console.WriteLine($"entityType: {entityType}");

			foreach (var selection in selections)
			{
				var dtoProperty = (PropertyInfo)selection.Field.Member!;
				var entityProperty = entityType.GetProperty(dtoProperty.Name)!; // TODO: Improve this logic
				var entityPropertyAccess = Expression.Property(sourceExpr, entityProperty);

				if (selection.Type.IsLeafType())
				{
					assignments.Add(Expression.Bind(dtoProperty, entityPropertyAccess));
				}
				else
				{
					var objectType = (IObjectType)selection.Type.NamedType();
					var innerSelections = context.GetSelections(objectType, selection);

					if (selection.Type.IsListType())
					{
						var e = typeDict[objectType.RuntimeType];
						var param = Expression.Parameter(e);
						var init = Project(innerSelections, param);
						var lambda = Expression.Lambda(init, param);
						var select = Expression.Call( // NOTE: https://stackoverflow.com/a/51896729
							typeof(Enumerable),
							nameof(Enumerable.Select),
							new Type[] { e, lambda.Body.Type },
							entityPropertyAccess, lambda // NOTE: `propertyExpr` here is what gets passed to `Select` as its `this` argument, and `lambda` is the lambda that gets passed to it.
						);
						assignments.Add(Expression.Bind(dtoProperty, select));
					}
					else
					{
						var memberInit = Project(innerSelections, entityPropertyAccess);
						assignments.Add(Expression.Bind(dtoProperty, memberInit));
					}
				}

				if (selection.Field.ContextData.GetValueOrDefault(MetaContextKey) is IEnumerable<AuthRule> authRules)
				{
					metaExprs = authRules.ToDictionary(
						r => r.Key,
						r => ReplacingExpressionVisitor.Replace(
							r.Rule.Parameters.First(), // NOTE: We assume there's only one parameter
							sourceExpr,
							r.Rule.Body
						)
					);
				}
			}

			if (metaExprs is not null)
			{
				var dictType = typeof(Dictionary<string, bool>);
				var dictInit = Expression.ListInit(
					Expression.New(dictType),
					metaExprs.Select(ex => Expression.ElementInit(
						dictType.GetMethod("Add")!,
						Expression.Constant(ex.Key), ex.Value
					))
				);
				assignments.Add(Expression.Bind(
					dtoType.GetProperty(nameof(BaseDto._Meta))!,
					dictInit
				));
			}

			return Expression.MemberInit(
				Expression.New(dtoType),
				assignments
			);
		}

		// TODO: Add null checks (for to-one relations) and inheritance checks

		var param = Expression.Parameter(typeDict[type.RuntimeType]);
		var body = Project(topLevelSelections, param);
		var lambda = Expression.Lambda<Func<Book, BookDto>>(body, param);

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine($"EXPRESSION: {lambda.ToReadableString()}");

		var result = await query.Select(lambda).FirstOrDefaultAsync();

		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine($"RESULT: {JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })}");
		Console.ResetColor();

		context.Result = result;

		Console.WriteLine("----------");
	}
}
