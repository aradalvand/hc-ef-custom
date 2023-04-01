var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextPool<AppDbContext>(o => o
	.UseNpgsql(@"Host=localhost;Username=postgres;Password=123456;Database=hc_ef_custom")
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
// - No mappers, expression visitors + EF overhead of intermediate projections
// - No IsProjected() on IDs + authorization rules will be fields inserted into the projection
// - Fields like currentUserPositionOnCourse could be part of their parent type (User)
// - Inheritance would work (Lesson, VideoLesson, ArticleLesson)
// - We could use services in the projections
