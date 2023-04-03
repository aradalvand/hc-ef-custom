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
			var objType = (IObjectType)selection.Type.NamedType();
			var dtoType = objType.RuntimeType;
			var entityType = _typeDict[dtoType];

			if (sourceExpression.Type.IsAssignableTo(typeof(IEnumerable<object>)))
			{
				var param = Expression.Parameter(entityType);
				var body = Project(param, selection);
				var lambda = Expression.Lambda(body, param);
				var select = Expression.Call(
					sourceExpression.Type.IsAssignableTo(typeof(IQueryable<object>))
						? typeof(Queryable)
						: typeof(Enumerable),
					nameof(Enumerable.Select),
					new Type[] { entityType, dtoType },
					sourceExpression, lambda
				);
				return select;
			}

			List<MemberAssignment> assignments = new();
			foreach (var subSelection in context.GetSelections(objType, selection))
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
