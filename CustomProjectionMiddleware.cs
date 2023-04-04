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
			var dtoType = selection.Type.NamedType().ToRuntimeType();
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

			List<MemberInitExpression> memberInitExpressions = new();
			foreach (var objectType in context.Operation.GetPossibleTypes(selection))
			{
				var objectTypeDtoType = objectType.RuntimeType;
				var objectTypeEntityType = _typeDict[objectType.RuntimeType];

				List<MemberAssignment> assignments = new();
				Dictionary<string, Expression> metaExpressions = new();
				foreach (var subSelection in context.GetSelections(objectType, selection))
				{
					if (subSelection.Field.IsIntrospectionField)
						continue;

					var dtoProperty = (PropertyInfo)subSelection.Field.Member!;
					var entityProperty = objectTypeEntityType.GetProperty(dtoProperty.Name)!; // TODO: Improve this logic

					var foo = sourceExpression.Type == objectTypeEntityType
						? sourceExpression
						: Expression.Convert(sourceExpression, objectTypeEntityType);
					var entityPropertyAccess = Expression.Property(foo, entityProperty);

					if (subSelection.SelectionSet is null)
					{
						var assignment = Expression.Bind(dtoProperty, entityPropertyAccess);
						assignments.Add(assignment);
					}
					else
					{
						var subProjection = Project(entityPropertyAccess, subSelection);
						var assignment = Expression.Bind(
							dtoProperty,
							IsNullable(entityProperty) // NOTE: Assumes that the nullability of the type of the entity property actually matches the nullability of the corresponding thing in the database; which is true in our case, but this is a mere assumption nonetheless.
								? Expression.Condition(
									Expression.Equal(entityPropertyAccess, Expression.Constant(null)),
									Expression.Constant(null, subProjection.Type), // NOTE: We have to pass the type
									subProjection
								)
								: subProjection
						);
						assignments.Add(assignment);
					}

					if (subSelection.Field.ContextData.GetValueOrDefault(MetaContextKey) is not IEnumerable<AuthRule> authRules)
						continue;

					foreach (var rule in authRules.Where(r => r.ShouldApply?.Invoke(subSelection) ?? true))
					{
						metaExpressions.Add(
							rule.Key,
							ReplacingExpressionVisitor.Replace(
								rule.Expression.Parameters.First(), // NOTE: We assume there's only one parameter
								foo,
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

			if (memberInitExpressions.Count == 1)
				return memberInitExpressions.Single();

			ConditionalExpression Conditionalize(int index = 0)
			{
				var current = memberInitExpressions[index];
				var condition = Expression.Condition(
					Expression.TypeIs(sourceExpression, _typeDict[current.Type]),
					Expression.Convert(current, dtoType),
					index == memberInitExpressions.Count - 1 // NOTE: If last index
						? Expression.Constant(null, dtoType)
						: Conditionalize(index + 1)
				);
				return condition;
			}
			var conditional = Conditionalize();
			return conditional;
		}
	}

	// --- Private Helpers:
	// NOTE: https://stackoverflow.com/q/58453972
	private static bool IsNullable(PropertyInfo propertyInfo)
		=> new NullabilityInfoContext().Create(propertyInfo).WriteState is NullabilityState.Nullable;
}
