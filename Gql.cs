﻿using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Resolvers;
using AgileObjects.ReadableExpressions;
using HotChocolate.Execution.Processing;
using System.Runtime.CompilerServices;
using HotChocolate.Types.Descriptors;
using System.Text.Json;

namespace hc_ef_custom.Types;

public class ProjectionResult<T>
{
	public T Main { get; set; } = default!;
	public Dictionary<string, object> Auth { get; set; } = default!;
}

[QueryType]
public static class Query
{
	public const string AuthContextKey = "Auth";

	[UseCustomProjection(ResultType.Single)]
	public static IQueryable<Book?> GetBook(AppDbContext db, int id) =>
		db.Books.Where(b => b.Id == id);

	public static async Task<Book?> GetBook2(AppDbContext db, int id)
	{
		var result = await db.Books
			.Where(b => b.Id == id)
			.Select(b => new ProjectionResult<BookDto>
			{
				Main = new()
				{
					Title = b.Title,
					Ratings = b.Ratings.Select(r => new BookRatingDto
					{
						Rating = r.Rating,
					}),
				},
				Auth = new()
				{
					{ "Title_StartsWithCrap",  b.Title.StartsWith("Crap") },
					{ "Ratings", b.Ratings.Select(r => new Dictionary<string, object> {
						{ "Rating_IsGreaterThan3", r.Rating > 3 }
					}) }
				},
			})
			.FirstOrDefaultAsync();
		Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
		return null;
	}

	// public static async Task<Author?> GetAuthor(
	// 	AppDbContext db,
	// 	IResolverContext context,
	// 	int id
	// )
	// {
	// 	await db.Authors.Where(b => b.Id == id)
	// 		.ProjectCustom(context, ResultType.Single);

	// 	return null;
	// }
}

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

public class UseCustomProjection : ObjectFieldDescriptorAttribute
{
	private ResultType _resultType;
	public UseCustomProjection(ResultType resultType, [CallerLineNumber] int order = 0)
	{
		_resultType = resultType;
		Order = order;
	}

	protected override void OnConfigure(
		IDescriptorContext context,
		IObjectFieldDescriptor descriptor,
		MemberInfo member
	)
	{
		descriptor.Type<BookType>();
		descriptor.Extend().OnBeforeCreate((context, definition) =>
		{
			// https://github.com/ChilliCream/graphql-platform/blob/main/src/HotChocolate/Data/src/Data/Projections/Extensions/SingleOrDefaultObjectFieldDescriptorExtensions.cs
			// var typeInfo = context.TypeInspector.CreateTypeInfo(definition.ResultType!);
			// Console.WriteLine($"typeInfo: {typeInfo}");

			// var typeRef = context.TypeInspector.GetTypeRef(typeInfo.NamedType, TypeContext.Output);
			// Console.WriteLine($"typeRef: {typeRef}");
			// definition.Type = typeRef;
		});
		descriptor.Use<CustomProjectionMiddleware>();
	}
}

public enum ResultType
{
	Single,
	Multiple
}

// Benefits of this approach:
// - No boxing
// - No "materializing" logic
// - We can directly return the result with no modification
// - Inheritance checks and results would be easier
public class BookDto
{
	public int Id { get; init; } = default!;
	public string Title { get; init; } = default!;
	public double AverageRating { get; init; } = default!;
	public AuthorDto Author { get; init; } = default!;
	public IEnumerable<BookRatingDto> Ratings { get; init; } = default!;
}
public class AuthorDto
{
	public int Id { get; init; } = default!;
	public string FirstName { get; init; } = default!;
	public string LastName { get; init; } = default!;
	public string FullName { get; init; } = default!;
}
public class BookRatingDto
{
	public int Id { get; init; } = default!;
	public byte Rating { get; init; } = default!;
}
// public record BookDto(
// 	int Id = default!,
// 	string Title = default!
// );

public class BookType : ObjectType<BookDto>
{
	protected override void Configure(IObjectTypeDescriptor<BookDto> descriptor)
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
