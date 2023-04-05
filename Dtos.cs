using System.ComponentModel;
using System.Linq.Expressions;
using hc_ef_custom.Types;

namespace hc_ef_custom;

// Benefits of this approach:
// - No boxing
// - No "materializing" logic
// - We can directly return the result with no modification
// - Inheritance checks and results would be easier
public abstract class BaseDto
{
	public int Id { get; init; } = default!;

	[GraphQLIgnore]
	[EditorBrowsable(EditorBrowsableState.Never)]
	public IDictionary<string, bool> _Meta { get; init; } = default!;
}
public class CourseDto : BaseDto
{
	public string Title { get; init; } = default!;
	public double AverageRating { get; init; } = default!;
	public int LessonsCount { get; init; } = default!;
	public InstructorDto Instructor { get; init; } = default!;
	public IEnumerable<LessonDto> Lessons { get; init; } = default!;
}

public class RatingDto : BaseDto
{
	public CourseDto Course { get; set; } = default!;
	public byte Stars { get; init; } = default!;
}

public class InstructorDto : BaseDto
{
	public string FirstName { get; init; } = default!;
	public string LastName { get; init; } = default!;
	public string FullName { get; init; } = default!;
	public IEnumerable<CourseDto> Courses { get; init; } = default!;
}

public abstract class LessonDto : BaseDto
{
	public string Title { get; set; } = default!;

	public CourseDto Course { get; set; } = default!;
}

public class VideoLessonDto : LessonDto
{
	public Uri Url { get; set; } = default!;
}

public class ArticleLessonDto : LessonDto
{
	public string Text { get; set; } = default!;
}
// ---
// public class CourseType : MappedObjectType<CourseDto, Course>
public class CourseType : ObjectType<CourseDto>
{
	protected override void Configure(IObjectTypeDescriptor<CourseDto> descriptor)
	{
		descriptor.Mapped().To<Course>(d =>
		{
			// d.Map(c => c.LessonsCount).To(c => c.Lessons.Count);
			// d.UseAuth(c => c.LessonsCount)
			// 	.MustBeAuthenticated();
			// 	.Must(c => c.Ratings.Any(r => r.Stars > 3));

			d.Property(c => c.LessonsCount).MapTo(c => c.Lessons.Count)
				.UseAuth(x => x
					.MustBeAuthenticated()
					.Must(c => c.Ratings.Any(r => r.Stars > 3))
				);
		});
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
