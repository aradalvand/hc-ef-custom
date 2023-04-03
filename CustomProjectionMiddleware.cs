using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Resolvers;
using AgileObjects.ReadableExpressions;
using HotChocolate.Execution.Processing;
using System.Text.Json;
using hc_ef_custom.Types;
using Microsoft.EntityFrameworkCore.Query;
using System.Collections;

namespace hc_ef_custom;

public class CustomProjectionMiddleware
{
	public const string MetaContextKey = "Meta";

	private readonly FieldDelegate _next;
	private readonly ResultType _resultType;

	private Dictionary<Type, Type> _typeDict = new() // TEMP
	{
		[typeof(BookDto)] = typeof(Book),
		[typeof(AuthorDto)] = typeof(Author),
		[typeof(BookRatingDto)] = typeof(BookRating),
	};

	public CustomProjectionMiddleware(FieldDelegate next, ResultType resultType)
	{
		_next = next;
		_resultType = resultType;
	}

	public async Task Invoke(IMiddlewareContext context)
	{
		await _next(context);

		if (context.Result is not IQueryable<object> query)
			throw new InvalidOperationException();

		Expression Project(Expression sourceExpression, ISelection selection)
		{
			var objectType = (IObjectType)selection.Type.NamedType();
			var dtoType = objectType.RuntimeType;
			var entityType = _typeDict[dtoType];

			if (sourceExpression.Type.IsAssignableTo(typeof(IEnumerable)))
			{
				var param = Expression.Parameter(entityType);
				var body = Project(param, selection);
				var lambda = Expression.Lambda(body, param);
				var select = Expression.Call(
					sourceExpression.Type.IsAssignableTo(typeof(IQueryable))
						? typeof(Queryable)
						: typeof(Enumerable),
					nameof(Enumerable.Select),
					new Type[] { entityType, dtoType },
					sourceExpression, lambda
				);
				return select;
			}

			List<MemberAssignment> assignments = new();
			Dictionary<string, Expression> metaExpressions = new();
			foreach (var subSelection in context.GetSelections(objectType, selection))
			{
				var dtoProperty = (PropertyInfo)subSelection.Field.Member!;
				var entityProperty = entityType.GetProperty(dtoProperty.Name)!; // TODO: Improve this logic
				var entityPropertyAccess = Expression.Property(sourceExpression, entityProperty);

				var assignment = Expression.Bind(
					dtoProperty,
					subSelection.Type.IsLeafType()
						? entityPropertyAccess
						: Project(entityPropertyAccess, subSelection)
				);
				assignments.Add(assignment);

				if (subSelection.Field.ContextData.GetValueOrDefault(MetaContextKey) is not IEnumerable<AuthRule> authRules)
					continue;

				foreach (var rule in authRules.Where(r => r.ShouldApply?.Invoke(subSelection) ?? true))
				{
					metaExpressions.Add(
						rule.Key,
						ReplacingExpressionVisitor.Replace(
							rule.Expression.Parameters.First(), // NOTE: We assume there's only one parameter
							sourceExpression,
							rule.Expression.Body
						)
					);
				}
			}
			if (metaExpressions.Any())
			{
				var dictType = typeof(Dictionary<string, bool>);
				var dictInit = Expression.ListInit(
					Expression.New(dictType),
					metaExpressions.Select(ex => Expression.ElementInit(
						dictType.GetMethod("Add")!,
						Expression.Constant(ex.Key), ex.Value
					))
				);
				assignments.Add(Expression.Bind(
					dtoType.GetProperty(nameof(BaseDto._Meta))!,
					dictInit
				));
			}
			var memberInit = Expression.MemberInit(
				Expression.New(dtoType),
				assignments
			);
			return memberInit;
		}

		query = query.Provider.CreateQuery<object>(Project(query.Expression, context.Selection));
		context.Result = _resultType switch
		{
			ResultType.Single => await query.FirstOrDefaultAsync(),
			ResultType.Multiple => await query.ToListAsync(),
			_ => throw new ArgumentOutOfRangeException(),
		};

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine(query.Expression.ToReadableString());
		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine(JsonSerializer.Serialize(context.Result, new JsonSerializerOptions { WriteIndented = true }));
		Console.ResetColor();
		Console.WriteLine("----------");
	}

	// --- Private Helpers:
	// NOTE: https://stackoverflow.com/q/58453972
	private static bool IsNullable(PropertyInfo propertyInfo)
		=> new NullabilityInfoContext().Create(propertyInfo).WriteState is NullabilityState.Nullable;
}
