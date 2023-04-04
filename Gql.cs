using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Execution.Processing;
using System.Runtime.CompilerServices;
using HotChocolate.Types.Descriptors;
using System.Text.Json;
using System.Diagnostics;
using System.ComponentModel;
using AgileObjects.ReadableExpressions;

namespace hc_ef_custom.Types;

public class ProjectionResult<T>
{
	public T Main { get; set; } = default!;
	public Dictionary<string, object> Auth { get; set; } = default!;
}

[QueryType]
public static class Query
{
	[UseCustomProjection<CourseType>(ResultType.Single)]
	public static IQueryable<Course?> GetCourse(AppDbContext db, int id) =>
		db.Courses.Where(b => b.Id == id);

	[UseCustomProjection<InstructorType>(ResultType.Single)]
	public static IQueryable<Instructor?> GetInstructor(AppDbContext db, int id) =>
		db.Instructors.Where(a => a.Id == id);

	[UseCustomProjection<ListType<LessonType>>(ResultType.Multiple)]
	public static IQueryable<Lesson> GetLessons(AppDbContext db) =>
		db.Lessons;

	[UseTestAttribute]
	[UseProjection]
	public static IQueryable<Course> GetCourses2(AppDbContext db)
	{
		return db.Courses;
	}
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

public class UseTestAttribute : ObjectFieldDescriptorAttribute
{
	public UseTestAttribute([CallerLineNumber] int order = 0)
	{
		Order = order;
	}

	protected override void OnConfigure(
		IDescriptorContext context,
		IObjectFieldDescriptor descriptor,
		MemberInfo member
	)
	{
		descriptor.Use(next => async context =>
		{
			await next(context);
			Console.WriteLine($"context.Result: {context.Result}");
			if (context.Result is IQueryable<object> query)
				Console.WriteLine($"query.Expression: {query.Expression.ToReadableString()}");
		});
	}
}

public enum ResultType
{
	Single,
	Multiple
}

public class CourseType : ObjectType<CourseDto>
{
	protected override void Configure(IObjectTypeDescriptor<CourseDto> descriptor)
	{
		// descriptor.Field(b => b.Title)
		// 	.Auth(b => b.Ratings.Any(r => r.Rating > 3));
	}
}
public class InstructorType : ObjectType<InstructorDto>
{
	protected override void Configure(IObjectTypeDescriptor<InstructorDto> descriptor)
	{
	}
}
public class RatingType : ObjectType<RatingDto>
{
	protected override void Configure(IObjectTypeDescriptor<RatingDto> descriptor)
	{
	}
}
public class LessonType : InterfaceType<LessonDto>
{
	protected override void Configure(IInterfaceTypeDescriptor<LessonDto> descriptor)
	{
	}
}
public class VideoLessonType : ObjectType<VideoLessonDto>
{
	protected override void Configure(IObjectTypeDescriptor<VideoLessonDto> descriptor)
	{
	}
}
public class ArticleLessonType : ObjectType<ArticleLessonDto>
{
	protected override void Configure(IObjectTypeDescriptor<ArticleLessonDto> descriptor)
	{
	}
}

// public static class ObjectFieldDescriptorExtensions
// {
// 	public static IObjectFieldDescriptor Auth(
// 		this IObjectFieldDescriptor descriptor,
// 		Expression<Func<Course, bool>> ruleExpr
// 	)
// 	{
// 		const string key = "Foo";
// 		AuthRule rule = new(key, ruleExpr);
// 		descriptor.Extend().OnBeforeCreate(d =>
// 		{
// 			if (d.ContextData.GetValueOrDefault(CustomProjectionMiddleware.MetaContextKey) is List<AuthRule> authRules)
// 				authRules.Add(rule);
// 			else
// 				d.ContextData[CustomProjectionMiddleware.MetaContextKey] = new List<AuthRule> { rule };
// 		});
// 		descriptor.Use(next => async context =>
// 		{
// 			await next(context);
// 			var result = context.Parent<CourseDto>()._Meta[key];
// 			if (result)
// 				Console.WriteLine("Permitted.");
// 			else
// 				Console.WriteLine("Not permitted.");
// 		});

// 		return descriptor;
// 	}
// }

public record AuthRule(
	string Key,
	LambdaExpression Expression,
	Func<ISelection, bool>? ShouldApply = null
);
