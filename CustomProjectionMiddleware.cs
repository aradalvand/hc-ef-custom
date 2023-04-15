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
				foreach (ISelection childSelection in context.GetSelections(objectType, selection))
				{
					if (childSelection.Field.IsIntrospectionField)
						continue;

					PropertyInfo dtoProperty = (PropertyInfo)childSelection.Field.Member!; // NOTE: We can safely assume that the field corresponds to a property.

					var propertyLambda = Mappings.PropertyExpressions[dtoProperty]; // NOTE: Here we use the dictionary's indexer, meaning that that we're basically assuming that an entry exists in the dictionary for every property, because it "should".

					var sourceExpressionConverted =
						sourceExpression.Type != propertyLambda.Parameters.Single().Type // NOTE: We can safely assume there's only one parameter
							? Expression.Convert(sourceExpression, objectTypeEntityType)
							: sourceExpression;

					if (Mappings.PropertyAuthRules.TryGetValue(dtoProperty, out var authRules))
					{
						// NOTE: We filter out the rules that don't apply.
						authRules = authRules
							.Where(r => r.ShouldApply?.Invoke(context, childSelection) ?? true) // NOTE: If the `ShouldApply` func is null, that means the rule should apply in all circumstances.
							.ToList();

						bool skipProjection = false;
						foreach (var rule in authRules.OfType<PreAuthRule>())
						{
							bool permitted = rule.IsPermitted(await authRetriever.User);
							context.SetScopedState(rule.Key, permitted);

							if (!permitted)
							{
								skipProjection = true;
								break; // NOTE: As soon as one pre auth rule fails, we short-circuit; effectively only reporting the first failed pre auth rule.
							}
						}
						if (skipProjection) // NOTE: If any of the pre auth rules fail, then we don't include the property/field in the projection, including any of its meta auth rules. So we skip the rest of the containing `foreach` loop.
							continue;

						foreach (var rule in authRules.OfType<MetaAuthRule>())
						{
							var ruleLambda = rule.GetExpression(await authRetriever.User);
							var ruleExpr = ReplacingExpressionVisitor.Replace(
								ruleLambda.Parameters.Single(),
								sourceExpressionConverted,
								ruleLambda.Body
							);
							metaExpressions.Add(rule.Key, ruleExpr);
						}
					}

					var propertyExpression = ReplacingExpressionVisitor.Replace(
						propertyLambda.Parameters.Single(),
						sourceExpressionConverted,
						propertyLambda.Body
					);

					if (childSelection.SelectionSet is null)
					{
						var assignment = Expression.Bind(dtoProperty, propertyExpression);
						assignments.Add(assignment);
					}
					else
					{
						var subProjection = await ProjectAsync(propertyExpression, childSelection);
						var assignment = Expression.Bind(
							dtoProperty,
							// NOTE: Assumes that the nullability of the type of the entity property actually matches the nullability of the corresponding thing in the database; which is true in our case, but this is a mere assumption nonetheless.
							IsNullable(dtoProperty) // TODO
								? Expression.Condition(
									Expression.Equal(propertyExpression, Expression.Constant(null)),
									Expression.Constant(null, subProjection.Type), // NOTE: We have to pass the type
									subProjection
								)
								: subProjection
						);
						assignments.Add(assignment);
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
