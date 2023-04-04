var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextPool<AppDbContext>(o => o
	.UseNpgsql(
		@"Host=localhost;Username=postgres;Password=123456;Database=hc_ef_custom"
	// o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
	)
	.UseProjectables()
);

builder.Services
	.AddGraphQLServer()
	.AddProjections() // TODO: What is `IProjectionConvention` and `IProjectionProvider`?
	.ModifyRequestOptions(o =>
		o.IncludeExceptionDetails = builder.Environment.IsDevelopment()
	)
	.AddTypes()
	.RegisterDbContext<AppDbContext>();

var app = builder.Build();
app.MapGraphQL();

app.Run();

// Potential advantages:
// - Projection of getter-only projectable properties
// - No mappers, expression visitors + EF overhead of intermediate projections
// - No IsProjected() on IDs + authorization rules will be fields inserted into the projection
// - Some way we could apply an auth rule only if a specific inner field is selected (e.g. `Course.Video` and its `Id`)
// - Still be able to defer basic user info retrieval
// - Fields like currentUserPositionOnCourse could be part of their parent type (User)
// - Inheritance would work (Lesson, VideoLesson, ArticleLesson) + No EF "AsNoTracking" error in for owned types
// - We could use services in the projections
// - (Maybe not) We may be able to remove some fields on the `Query` type and for example to get info about a course's instructor, we just do `courseById(id: "") { instructor { ... } }`
