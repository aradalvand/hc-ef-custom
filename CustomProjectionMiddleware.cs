using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Resolvers;
using AgileObjects.ReadableExpressions;
using HotChocolate.Execution.Processing;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Query;

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

	public async Task Invoke(IMiddlewareContext context, AuthRetriever authRetriever)
	{
		await _next(context);

		if (context.Result is not IQueryable<object> query)
			return;

		query = query.Provider.CreateQuery<object>(
			await ProjectAsync(query.Expression, context.Selection)
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

		// NOTE: Note that we do as little reflection here as we can, we aim to keep the middleware as reflection-free as possible.
		// NOTE: `ValueTask` seems to be a more appropriate return type for this method since most of the time it actually completes synchronously.
		async ValueTask<Expression> ProjectAsync(Expression sourceExpression, ISelection selection)
		{
			INamedType type = selection.Type.NamedType(); // NOTE: Effectively is either an interface type or an object type — shouldn't be a scalar for example — and is a "mapped type" configured via the `Mapped()` method on `IObjectTypeFieldDescriptor`.
			Type dtoType = type.ToRuntimeType(); // NOTE: There's always a "DTO" CLR type behind every mapped type.
			Type entityType = Mappings.Types[dtoType];

			if (sourceExpression.Type.IsAssignableTo(typeof(IEnumerable<object>))) // NOTE: If the source expression is a "set", if you will, we wrap the projection in a `Select(elm => ...)` call.
			{
				var param = Expression.Parameter(entityType);
				var body = await ProjectAsync(param, selection);
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
				Type objectTypeEntityType = Mappings.Types[objectTypeDtoType];

				List<MemberAssignment> assignments = new();
				Dictionary<string, Expression> metaExpressions = new();
				foreach (ISelection subSelection in context.GetSelections(objectType, selection))
				{
					if (subSelection.Field.IsIntrospectionField)
						continue;

					PropertyInfo dtoProperty = (PropertyInfo)subSelection.Field.Member!; // NOTE: We can safely assume that the field corresponds to a property.

					// TODO: How about we do this in the field middlewares?
					if (Mappings.PropertyAuthPreRules.TryGetValue(dtoProperty, out var preRules))
					{
						foreach (PreAuthRule preRule in preRules)
						{
							if (preRule.ShouldApply?.Invoke(context, subSelection) == false)
								continue;

							bool passed = preRule.Check(await authRetriever.GetAsync());
							if (!passed)
							{
								// TODO: Accumulate all the errors or just report the first one?
								throw new GraphQLException(new Error(
									message: "دسترسی مجاز نیست.",
									code: "NOT_AUTHORIZED"
								));
							}
						}
					}

					var propertyLambda = Mappings.PropertyExpressions[dtoProperty]; // NOTE: Here we use the dictionary's indexer, meaning that that we're basically assuming that an entry exists in the dictionary for every property, because it "should".

					var sourceExpressionConverted =
						sourceExpression.Type != propertyLambda.Parameters.Single().Type // NOTE: We can safely assume there's only one parameter
							? Expression.Convert(sourceExpression, objectTypeEntityType)
							: sourceExpression;

					var fieldExpression = ReplacingExpressionVisitor.Replace(
						propertyLambda.Parameters.Single(),
						sourceExpressionConverted,
						propertyLambda.Body
					);

					if (subSelection.SelectionSet is null)
					{
						var assignment = Expression.Bind(dtoProperty, fieldExpression);
						assignments.Add(assignment);
					}
					else
					{
						var subProjection = await ProjectAsync(fieldExpression, subSelection);
						var assignment = Expression.Bind(
							dtoProperty,
							// NOTE: Assumes that the nullability of the type of the entity property actually matches the nullability of the corresponding thing in the database; which is true in our case, but this is a mere assumption nonetheless.
							IsNullable(dtoProperty) // TODO
								? Expression.Condition(
									Expression.Equal(fieldExpression, Expression.Constant(null)),
									Expression.Constant(null, subProjection.Type), // NOTE: We have to pass the type
									subProjection
								)
								: subProjection
						);
						assignments.Add(assignment);
					}

					if (Mappings.PropertyAuthRules.TryGetValue(dtoProperty, out var authRules))
					{
						foreach (AuthRule rule in authRules)
						{
							if (rule.ShouldApply?.Invoke(context, subSelection) == false) // NOTE: If the `ShouldApply` func is null, that means the rule should apply. That's what the explicit "== false" here does.
								continue;

							var ruleLambda = rule.ExpressionResolver(await authRetriever.GetAsync());
							metaExpressions.Add(
								rule.Key,
								ReplacingExpressionVisitor.Replace(
									ruleLambda.Parameters.Single(),
									sourceExpressionConverted,
									ruleLambda.Body
								)
							);
						}
					}
				}
				if (metaExpressions.Any())
				{
					// TODO: Use C# 12's type aliases here
					Type dictType = typeof(Dictionary<string, bool>);
					var dictInit = Expression.ListInit(
						Expression.New(dictType),
						metaExpressions.Select(ex => Expression.ElementInit(
							dictType.GetMethod(nameof(Dictionary<string, bool>.Add))!,
							Expression.Constant(ex.Key), ex.Value
						))
					);
					var metaAssignment = Expression.Bind(
						objectTypeDtoType.GetProperty(nameof(BaseDto._Meta))!,
						dictInit
					);
					assignments.Add(metaAssignment);
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
							Mappings.Types[current.Key.RuntimeType]
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
