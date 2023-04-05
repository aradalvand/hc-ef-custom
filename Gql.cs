using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Execution.Processing;
using System.Runtime.CompilerServices;
using HotChocolate.Types.Descriptors;
using AgileObjects.ReadableExpressions;

namespace hc_ef_custom.Types;

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
	public static IQueryable<Lesson> GetLessons2(AppDbContext db)
	{
		return db.Lessons;
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
