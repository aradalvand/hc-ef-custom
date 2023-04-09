using EntityFrameworkCore.Projectables;

namespace hc_ef_custom;

public class AppDbContext : DbContext
{
	public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
	{
	}

	protected override void OnModelCreating(ModelBuilder builder)
	{
		builder.Entity<Lesson>()
			.HasDiscriminator<string>("Type")
				.HasValue<VideoLesson>("video")
				.HasValue<ArticleLesson>("article");
	}

	public DbSet<Course> Courses => Set<Course>();
	public DbSet<Instructor> Instructors => Set<Instructor>();
	public DbSet<Rating> Ratings => Set<Rating>();
	public DbSet<Lesson> Lessons => Set<Lesson>();
}

public abstract class BaseEntity
{
	public int Id { get; set; }
}

public class Course : BaseEntity
{
	public required int InstructorId { get; set; }
	public required string Title { get; set; }

	[Projectable]
	public double AverageRating => Ratings.Average(r => r.Stars);

	public required Instructor Instructor { get; set; }
	public ICollection<Lesson> Lessons { get; set; } = default!;
	public ICollection<Rating> Ratings { get; set; } = default!;
}

public class Rating : BaseEntity
{
	public required int CourseId { get; set; }
	public required byte Stars { get; set; }

	public Course Course { get; set; } = default!;
}

public class Instructor : BaseEntity
{
	public required string FirstName { get; set; }
	public required string LastName { get; set; }

	[Projectable]
	public string FullName => FirstName + " " + LastName;

	public ICollection<Course> Courses { get; set; } = default!;
}

public abstract class Lesson : BaseEntity
{
	public required int CourseId { get; set; }
	public required string Title { get; set; }

	public Course Course { get; set; } = default!;
}

public class VideoLesson : Lesson
{
	public required Uri Url { get; set; }
}
public class ArticleLesson : Lesson
{
	public required string Text { get; set; }
}
