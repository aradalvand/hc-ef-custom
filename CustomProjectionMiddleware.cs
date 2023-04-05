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

	private Dictionary<Type, Type> _typeDict = new() // TEMP
	{
		[typeof(CourseDto)] = typeof(Course),
		[typeof(InstructorDto)] = typeof(Instructor),
		[typeof(RatingDto)] = typeof(Rating),
		[typeof(LessonDto)] = typeof(Lesson),
		[typeof(VideoLessonDto)] = typeof(VideoLesson),
		[typeof(ArticleLessonDto)] = typeof(ArticleLesson),
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

		Expression Project(Expression sourceExpression, ISelection selection)
		{
			Type dtoType = selection.Type.NamedType().ToRuntimeType();
			Type entityType = _typeDict[dtoType];

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

			List<MemberInitExpression> memberInitExpressions = new();
			foreach (IObjectType objectType in context.Operation.GetPossibleTypes(selection))
			{
				Type objectTypeDtoType = objectType.RuntimeType;
				Type objectTypeEntityType = _typeDict[objectType.RuntimeType];

				List<MemberAssignment> assignments = new();
				Dictionary<string, Expression> metaExpressions = new();
				foreach (ISelection subSelection in context.GetSelections(objectType, selection))
				{
					if (subSelection.Field.IsIntrospectionField)
						continue;

					PropertyInfo dtoProperty = (PropertyInfo)subSelection.Field.Member!;
					var expr = (LambdaExpression)subSelection.Field.ContextData["Foo"]!;
					PropertyInfo entityProperty = objectTypeEntityType.GetProperty(dtoProperty.Name)!; // TODO: Improve this logic

					var sourceExpressionConverted = sourceExpression.Type == objectTypeEntityType
						? sourceExpression
						: Expression.Convert(sourceExpression, objectTypeEntityType);

					var expr2 = ReplacingExpressionVisitor.Replace(
						expr.Parameters.First(),
						sourceExpressionConverted,
						expr.Body
					);

					if (subSelection.SelectionSet is null)
					{
						var assignment = Expression.Bind(dtoProperty, expr2);
						assignments.Add(assignment);
					}
					else
					{
						var subProjection = Project(expr2, subSelection);
						var assignment = Expression.Bind(
							dtoProperty,
							// NOTE: Assumes that the nullability of the type of the entity property actually matches the nullability of the corresponding thing in the database; which is true in our case, but this is a mere assumption nonetheless.
							subProjection is MemberInitExpression && IsNullable(entityProperty) // TODO: Make sure this is good
								? Expression.Condition(
									Expression.Equal(expr2, Expression.Constant(null)),
									Expression.Constant(null, subProjection.Type), // NOTE: We have to pass the type
									subProjection
								)
								: subProjection
						);
						assignments.Add(assignment);
					}

					if (subSelection.Field.ContextData.GetValueOrDefault(WellKnownContextKeys.Meta)
						is not IEnumerable<AuthRule> authRules)
						continue;

					foreach (var rule in authRules)
					{
						if (rule.ShouldApply?.Invoke(subSelection) == false)
							continue;

						metaExpressions.Add(
							rule.Key,
							ReplacingExpressionVisitor.Replace(
								rule.Expression.Parameters.First(), // NOTE: We assume there's only one parameter
								sourceExpressionConverted,
								rule.Expression.Body
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
				memberInitExpressions.Add(memberInit);
			}

			return memberInitExpressions.Count > 1
				? memberInitExpressions.Aggregate(
					Expression.Constant(null, dtoType) as Expression,
					(accumulator, current) => Expression.Condition(
						Expression.TypeIs(sourceExpression, _typeDict[current.Type]),
						Expression.Convert(current, dtoType), // NOTE: The conversion is necessary — or else we get an exception, the two sides of a ternary expression should be of the same type.
						accumulator
					)
				)
				: memberInitExpressions.Single();
		}
	}

	// --- Private Helpers:
	// NOTE: https://stackoverflow.com/q/58453972
	private static bool IsNullable(PropertyInfo propertyInfo)
		=> new NullabilityInfoContext().Create(propertyInfo).WriteState is NullabilityState.Nullable;
}
