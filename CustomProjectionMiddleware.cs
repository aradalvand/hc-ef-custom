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
	private readonly FieldDelegate _next;
	private readonly ResultType _resultType;

	public CustomProjectionMiddleware(FieldDelegate next, ResultType resultType)
	{
		_next = next;
		_resultType = resultType;
	}

	public async Task Invoke(IMiddlewareContext context)
	{
		await _next(context);

		if (context.Result is not IQueryable<object> query)
			return;

		query = query.Provider.CreateQuery<object>(
			Project(query.Expression, context.Selection)
		);

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

		// NOTE: Note that we do as little reflection here as possible, we aim to keep the middleware as reflection-free as possible.
		Expression Project(Expression sourceExpression, ISelection selection)
		{
			INamedType type = selection.Type.NamedType(); // NOTE: Effectively is either an interface type or an object type — shouldn't be a scalar for example — and is a "mapped type" configured via the `Mapped()` method on `IObjectTypeFieldDescriptor`.
			Type dtoType = type.ToRuntimeType(); // NOTE: There's always a "DTO" CLR type behind every mapped type.
			Type entityType = TypeMapping.Dictionary[dtoType];

			if (sourceExpression.Type.IsAssignableTo(typeof(IEnumerable<object>))) // NOTE: If the source expression is a "set", if you will, we wrap the projection in a `Select(elm => ...)` call.
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

			Dictionary<IObjectType, MemberInitExpression> objectProjections = new();
			foreach (IObjectType objectType in context.Operation.GetPossibleTypes(selection))
			{
				Type objectTypeDtoType = objectType.RuntimeType;
				Type objectTypeEntityType = TypeMapping.Dictionary[objectTypeDtoType];

				List<MemberAssignment> assignments = new();
				Dictionary<string, Expression> metaExpressions = new();
				foreach (ISelection subSelection in context.GetSelections(objectType, selection))
				{
					if (subSelection.Field.IsIntrospectionField)
						continue;

					PropertyInfo dtoProperty = (PropertyInfo)subSelection.Field.Member!;
					var fieldData = (MappedFieldData)subSelection.Field.ContextData[WellKnownContextKeys.MappedFieldData]!;

					var sourceExpressionConverted = sourceExpression.Type == objectTypeEntityType
						? sourceExpression
						: Expression.Convert(sourceExpression, objectTypeEntityType);

					var fieldExpression = ReplacingExpressionVisitor.Replace(
						fieldData.Expression.Parameters.First(),
						sourceExpressionConverted,
						fieldData.Expression.Body
					);

					if (subSelection.SelectionSet is null)
					{
						var assignment = Expression.Bind(dtoProperty, fieldExpression);
						assignments.Add(assignment);
					}
					else
					{
						var subProjection = Project(fieldExpression, subSelection);
						var assignment = Expression.Bind(
							dtoProperty,
							// NOTE: Assumes that the nullability of the type of the entity property actually matches the nullability of the corresponding thing in the database; which is true in our case, but this is a mere assumption nonetheless.
							IsNullable(dtoProperty) // todo
								? Expression.Condition(
									Expression.Equal(fieldExpression, Expression.Constant(null)),
									Expression.Constant(null, subProjection.Type), // NOTE: We have to pass the type
									subProjection
								)
								: subProjection
						);
						assignments.Add(assignment);
					}

					if (subSelection.Field.ContextData.GetValueOrDefault(WellKnownContextKeys.MappedFieldData)
						is not IEnumerable<AuthRule> authRules)
						continue;

					foreach (var rule in authRules)
					{
						if (rule.ShouldApply?.Invoke(subSelection) == false)
							continue;

						var ruleExpr = rule.Expression(null!); // todo
						metaExpressions.Add(
							rule.Key,
							ReplacingExpressionVisitor.Replace(
								ruleExpr.Parameters.First(), // NOTE: We assume there's only one parameter
								sourceExpressionConverted,
								ruleExpr.Body
							)
						);
					}
				}
				if (metaExpressions.Any())
				{
					Type dictType = typeof(Dictionary<string, bool>);
					var dictInit = Expression.ListInit(
						Expression.New(dictType),
						metaExpressions.Select(ex => Expression.ElementInit(
							dictType.GetMethod(nameof(Dictionary<string, bool>.Add))!,
							Expression.Constant(ex.Key), ex.Value
						))
					);
					assignments.Add(Expression.Bind(
						objectTypeDtoType.GetProperty(nameof(BaseDto._Meta))!,
						dictInit
					));
				}
				var memberInit = Expression.MemberInit(
					Expression.New(objectTypeDtoType),
					assignments
				);
				objectProjections.Add(objectType, memberInit);
			}

			return objectProjections.Count > 1
				? objectProjections.Aggregate(
					Expression.Constant(null, dtoType) as Expression,
					(accumulator, current) => Expression.Condition(
						Expression.TypeIs(
							sourceExpression,
							TypeMapping.Dictionary[current.Key.RuntimeType]
						),
						Expression.Convert(current.Value, dtoType), // NOTE: The conversion is necessary — or else we get an exception, the two sides of a ternary expression should be of the same type.
						accumulator
					)
				)
				: objectProjections.Single().Value;
		}
	}

	// --- Private Helpers:
	// NOTE: https://stackoverflow.com/q/58453972
	private static bool IsNullable(PropertyInfo propertyInfo)
		=> new NullabilityInfoContext().Create(propertyInfo).WriteState is NullabilityState.Nullable;
}
