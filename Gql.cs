namespace hc_ef_custom.Types;

[QueryType]
public class Query
{
	[UseProjector(ResultType.Single)]
	public IQueryable<Course> GetCourse(AppDbContext db, int id) =>
		db.Courses.Where(b => b.Id == id);

	[UseProjector(ResultType.Single)]
	public IQueryable<Instructor> GetInstructor(AppDbContext db, int id) =>
		db.Instructors.Where(a => a.Id == id);

	[UseProjector(ResultType.Multiple)]
	public IQueryable<Lesson> GetLessons(AppDbContext db) =>
		db.Lessons;
}
