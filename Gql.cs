using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Execution.Processing;
using System.Runtime.CompilerServices;
using HotChocolate.Types.Descriptors;
using System.Text.Json;
using System.Diagnostics;
using System.ComponentModel;

namespace hc_ef_custom.Types;

public class ProjectionResult<T>
{
	public T Main { get; set; } = default!;
	public Dictionary<string, object> Auth { get; set; } = default!;
}

[QueryType]
public static class Query
{
	[UseCustomProjection<BookType>(ResultType.Single)]
	public static IQueryable<Book?> GetBook(AppDbContext db, int id) =>
		db.Books.Where(b => b.Id == id);

	[UseCustomProjection<AuthorType>(ResultType.Single)]
	public static IQueryable<Author?> GetAuthor(AppDbContext db, int id) =>
		db.Authors.Where(a => a.Id == id);
}

public class UseCustomProjection<T> : ObjectFieldDescriptorAttribute where T : class, IOutputType
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
		descriptor.Type<T>();
		descriptor.Extend().OnBeforeCreate((context, definition) =>
		{
			// https://github.com/ChilliCream/graphql-platform/blob/main/src/HotChocolate/Data/src/Data/Projections/Extensions/SingleOrDefaultObjectFieldDescriptorExtensions.cs
			// var typeInfo = context.TypeInspector.CreateTypeInfo(definition.ResultType!);
			// Console.WriteLine($"typeInfo: {typeInfo}");

			// var typeRef = context.TypeInspector.GetTypeRef(typeInfo.NamedType, TypeContext.Output);
			// Console.WriteLine($"typeRef: {typeRef}");
			// definition.Type = typeRef;
		});
		descriptor.Use((_, next) => new CustomProjectionMiddleware(next, _resultType));
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
public abstract class BaseDto
{
	[GraphQLIgnore]
	[EditorBrowsable(EditorBrowsableState.Never)]
	public IDictionary<string, bool> _Meta { get; init; } = default!;
}
public class BookDto : BaseDto
{
	public int Id { get; init; } = default!;
	public string Title { get; init; } = default!;
	public double AverageRating { get; init; } = default!;
	public AuthorDto Author { get; init; } = default!;
	public IEnumerable<BookRatingDto> Ratings { get; init; } = default!;
}
public class AuthorDto : BaseDto
{
	public int Id { get; init; } = default!;
	public string FirstName { get; init; } = default!;
	public string LastName { get; init; } = default!;
	public string FullName { get; init; } = default!;
	public IEnumerable<BookDto> Books { get; init; } = default!;
}
public class BookRatingDto : BaseDto
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
		descriptor.Field(b => b.Title)
			.Auth(b => b.Title.StartsWith("Foo"));
	}
}
public class AuthorType : ObjectType<AuthorDto>
{
	protected override void Configure(IObjectTypeDescriptor<AuthorDto> descriptor)
	{
	}
}

public static class ObjectFieldDescriptorExtensions
{
	public static IObjectFieldDescriptor Auth(
		this IObjectFieldDescriptor descriptor,
		Expression<Func<Book, bool>> ruleExpr
	)
	{
		const string key = "Foo";
		AuthRule rule = new(key, ruleExpr);
		descriptor.Extend().OnBeforeCreate(d =>
		{
			if (d.ContextData.GetValueOrDefault(CustomProjectionMiddleware.MetaContextKey) is List<AuthRule> authRules)
				authRules.Add(rule);
			else
				d.ContextData[CustomProjectionMiddleware.MetaContextKey] = new List<AuthRule> { rule };
		});
		descriptor.Use(next => async context =>
		{
			await next(context);
			var result = context.Parent<BookDto>()._Meta[key];
			if (result)
				Console.WriteLine("Permitted.");
			else
				Console.WriteLine("Not permitted.");
		});

		return descriptor;
	}
}

public record AuthRule(
	string Key,
	LambdaExpression Expression,
	Func<ISelection, bool>? ShouldApply = null
);
