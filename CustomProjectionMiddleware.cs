using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Resolvers;
using AgileObjects.ReadableExpressions;
using HotChocolate.Execution.Processing;
using System.Text.Json;
using hc_ef_custom.Types;

namespace hc_ef_custom;

public class CustomProjectionMiddleware
{
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
		var param = Expression.Parameter(typeDict[type.RuntimeType]);

		var matParam = Expression.Parameter(typeof(object[]));

		var exprs = new List<Expression>();
		var matExprs = new List<MemberBinding>();

		int index = 0;
		foreach (var selection in topLevelSelections)
		{
			var dtoProperty = (PropertyInfo)selection.Field.Member!;
			var entityType = typeDict[selection.Field.DeclaringType.RuntimeType];
			var entityProperty = entityType.GetProperty(dtoProperty.Name)!; // TODO: Improve this logic
			var entityPropertyAccess = Expression.Property(param, entityProperty);
			exprs.Add(Expression.Convert(entityPropertyAccess, typeof(object)));
			matExprs.Add(Expression.Bind(
				dtoProperty,
				Expression.Convert(
					Expression.ArrayIndex(matParam, Expression.Constant(index)),
					dtoProperty.PropertyType
				)
			));
			index++;
		}

		var arrayInit = Expression.NewArrayInit(
			typeof(object),
			exprs
		);
		var lambda = Expression.Lambda(arrayInit, param);

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine($"PROJECTION: {lambda.ToReadableString()}");

		var dtoInit = Expression.MemberInit(
			Expression.New(typeof(BookDto)),
			matExprs
		);
		var matLambda = Expression.Lambda(dtoInit, matParam);
		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine($"MATERIALIZER: {matLambda.ToReadableString()}");

		var result = await query.Select((Expression<Func<Book, object[]>>)lambda).FirstOrDefaultAsync();

		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine($"RESULT: {JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })}");
		Console.ResetColor();

		var mat = ((Expression<Func<object[], BookDto>>)matLambda).Compile();
		context.Result = result is null ? null : mat(result);

		Console.WriteLine("----------");
	}

	public async Task InvokeTemp(IMiddlewareContext context)
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
		List<Expression> Project(IEnumerable<ISelection> selections, Expression on)
		{
			var exprs = new List<Expression>();
			foreach (var selection in selections)
			{
				var dtoProperty = (PropertyInfo)selection.Field.Member!;
				var entityType = typeDict[selection.Field.DeclaringType.RuntimeType];
				var entityProperty = entityType.GetProperty(dtoProperty.Name)!; // TODO: Improve this logic
				var entityPropertyAccess = Expression.Property(on, entityProperty);

				if (selection.Type.IsLeafType())
				{
					exprs.Add(Expression.Convert(entityPropertyAccess, typeof(object)));
				}
				else
				{
					var objectType = (IObjectType)selection.Type.NamedType();
					var innerSelections = context.GetSelections(objectType, selection);

					if (selection.Type.IsListType())
					{
						var e = typeDict[objectType.RuntimeType];
						var param = Expression.Parameter(e);
						var init = Expression.NewArrayInit(
							typeof(object),
							Project(innerSelections, param)
						);
						var lambda = Expression.Lambda(init, param);
						var select = Expression.Call( // NOTE: https://stackoverflow.com/a/51896729
							typeof(Enumerable),
							nameof(Enumerable.Select),
							new Type[] { e, lambda.Body.Type },
							entityPropertyAccess, lambda // NOTE: `propertyExpr` here is what gets passed to `Select` as its `this` argument, and `lambda` is the lambda that gets passed to it.
						);
						exprs.Add(select);
					}
					else
					{
						// var memberInit = Expression.NewArrayInit(
						// 	typeof(object),
						// 	Project(innerSelections, entityPropertyAccess)
						// );
						// exprs.Add(memberInit);
						exprs.AddRange(Project(innerSelections, entityPropertyAccess));
					}
				}
			}
			return exprs;
		}

		// TODO: The auth rules could be "deep", so we can't just designate a dictionary on the top-level for them. We probably have to use a "Tuple" either the built-in type or a special type, that holds both the actual object result, and the auth rules. Dictionary/new type
		// TODO: Add null checks (for to-one relations) and inheritance checks

		var type = (IObjectType)context.Selection.Type.NamedType();
		var topLevelSelections = context.GetSelections(type);
		var param = Expression.Parameter(typeDict[type.RuntimeType]);
		var dtoMemberInit = Expression.NewArrayInit(
			typeof(object),
			Project(topLevelSelections, param)
		);
		var lambda = Expression.Lambda(dtoMemberInit, param);

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine($"EXPRESSION: {lambda.ToReadableString()}");

		var result = await query.Select((Expression<Func<Book, object[]>>)lambda).FirstOrDefaultAsync();

		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine($"RESULT: {JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })}");
		Console.ResetColor();

		context.Result = null;

		Console.WriteLine("----------");
	}
}
